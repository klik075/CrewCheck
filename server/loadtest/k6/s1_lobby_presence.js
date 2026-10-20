/**
 * 시나리오 1 — 대기실 "멀쩡한 참가자가 튕기는" 문제 재현
 *
 * 무엇을 흉내 내나
 *   - ROOMS개 방 × PLAYERS명이 대기실에 앉아 있다. (게임 시작 X)
 *   - 실제 Unity처럼 WebSocket(/ws/v1)에 붙어 10초마다 ping, 방을 room.subscribe.
 *   - WebSocket 구독 중이므로 방 조회(GET /rooms/{id})는 POLL_SEC(기본 15초)마다만 한다. ← 대기실 접속 기록은 이 GET으로만 남는다
 *   - DROP_RATE 확률로 조회 한 번을 건너뛴다(모바일 네트워크 순간 끊김 흉내).
 *   - 선택: BROWSERS명이 로비에서 방 목록을 계속 새로고침해서 서버에 배경 부하를 준다.
 *
 * 무엇을 재나
 *   - lobby_false_kicks : 스스로 나가지 않았는데 방 명단에서 빠진 횟수 (0이어야 정상)
 *   - lobby_kick_after_s: 입장 후 몇 초 만에 튕겼나
 *   - lobby_poll_duration: 대기실 조회 응답 시간 (부하로 이게 늘어나면 기록이 늦게 찍힌다)
 *
 * 실행 예
 *   k6 run -e ROOMS=50 -e PLAYERS=4 -e HOLD=5m s1_lobby_presence.js            # 기준선
 *   k6 run -e ROOMS=50 -e DROP_RATE=0.1 s1_lobby_presence.js                   # 원래 장애 재현 (조회 누락)
 *   k6 run -e ROOMS=300 -e BROWSERS=200 -e DROP_RATE=0.05 s1_lobby_presence.js # 부하 + 누락
 *   k6 run -e WS=0 s1_lobby_presence.js                                        # 비교용: 예전 1초 폴링 클라이언트
 */
import ws from 'k6/ws';
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Trend, Rate } from 'k6/metrics';
import {
  WS_URL, guestLogin, createRoom, joinRoom, getRoom, leaveRoom, listRooms, isStillInRoom, randomBetween, authHeaders, BASE_URL,
} from './lib/common.js';

const ROOMS = Number(__ENV.ROOMS || 50);
const PLAYERS = Number(__ENV.PLAYERS || 4);          // 방장 포함 인원 (4~12)
const HOLD = __ENV.HOLD || '5m';                      // 대기실에 머무는 시간
const POLL_SEC = Number(__ENV.POLL_SEC || 15);        // Unity RealtimePolling.SafetyNetSeconds
const PING_SEC = Number(__ENV.PING_SEC || 10);
const DROP_RATE = Number(__ENV.DROP_RATE || 0);       // 조회 1회를 건너뛸 확률
const RAMP_SEC = Number(__ENV.RAMP_SEC || 30);        // 참가자 입장을 이 시간 안에 흩뿌림
const USE_WS = (__ENV.WS || '1') !== '0';
const BROWSERS = Number(__ENV.BROWSERS || 0);         // 배경 부하: 로비 방 목록 새로고침 사용자 수
const BROWSE_SEC = Number(__ENV.BROWSE_SEC || 2);

const holdSec = parseDuration(HOLD);

const falseKicks = new Counter('lobby_false_kicks');
const kickAfter = new Trend('lobby_kick_after_s');
const pollDuration = new Trend('lobby_poll_duration', true);
const pollGapMax = new Trend('lobby_poll_gap_max_s');   // 한 참가자가 겪은 "조회 간격" 최대값
const joinFailed = new Counter('lobby_join_failed');
const stayedRate = new Rate('lobby_stayed');            // 끝까지 남아 있던 비율

const scenarios = {
  waiting_room: {
    executor: 'per-vu-iterations',
    exec: 'waitingRoom',
    vus: ROOMS * PLAYERS,
    iterations: 1,
    maxDuration: `${holdSec + RAMP_SEC + 120}s`,
  },
};
if (BROWSERS > 0) {
  scenarios.lobby_browsers = {
    executor: 'constant-vus',
    exec: 'lobbyBrowser',
    vus: BROWSERS,
    duration: `${holdSec + RAMP_SEC}s`,
  };
}

export const options = {
  setupTimeout: '10m',
  scenarios,
  thresholds: {
    lobby_false_kicks: ['count==0'],
    'lobby_poll_duration': ['p(95)<500'],
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// ---------------------------------------------------------------------------
// setup: 방장 로그인 + 방 생성 (방 ID를 VU끼리 나누려면 setup에서 만들어야 한다)
// ---------------------------------------------------------------------------
export function setup() {
  const runId = `${Date.now()}`;
  const hosts = [];
  const CHUNK = 50;
  for (let start = 0; start < ROOMS; start += CHUNK) {
    const idx = [...Array(Math.min(CHUNK, ROOMS - start)).keys()].map((i) => start + i);
    // 로그인을 묶어서 보낸다
    const logins = http.batch(idx.map((i) => ['POST', `${BASE_URL}/api/members/guest`,
      JSON.stringify({ guestUuid: `lt-${runId}-h${i}` }),
      { headers: { 'Content-Type': 'application/json' }, tags: { name: 'POST /api/members/guest' } }]));
    const creates = http.batch(logins.map((r, k) => {
      const b = r.json();
      hosts[idx[k]] = { token: b.accessToken, userId: b.userId };
      return ['POST', `${BASE_URL}/api/v1/rooms`,
        JSON.stringify({ title: `부하테스트 ${runId.slice(-4)}-${idx[k]}`, maxPlayers: Math.max(PLAYERS, 4), privateRoom: false }),
        { ...authHeaders(b.accessToken), tags: { name: 'POST /api/v1/rooms' } }];
    }));
    creates.forEach((r, k) => {
      check(r, { 'setup room 201': (x) => x.status === 201 });
      hosts[idx[k]].roomId = r.json('id');
    });
  }
  // 방 생성 직후부터 45초 시계가 돈다. setup이 길어졌을 수 있으니 방장 기록을 한 번 갱신해 둔다.
  http.batch(hosts.map((h) => ['GET', `${BASE_URL}/api/v1/rooms/${h.roomId}`, null,
    { ...authHeaders(h.token), tags: { name: 'GET /api/v1/rooms/{id}' } }]));
  return { runId, hosts };
}

// ---------------------------------------------------------------------------
// 대기실 참가자
// ---------------------------------------------------------------------------
export function waitingRoom(data) {
  const n = __VU - 1;
  const roomIdx = Math.floor(n / PLAYERS);
  const slot = n % PLAYERS;
  const host = data.hosts[roomIdx];
  if (!host) return;
  const roomId = host.roomId;

  let me;
  if (slot === 0) {
    me = { token: host.token, userId: host.userId };
  } else {
    sleep(randomBetween(0, RAMP_SEC));
    me = guestLogin(`lt-${data.runId}-r${roomIdx}-p${slot}`);
    const res = joinRoom(me.token, roomId);
    if (!check(res, { 'join 200': (r) => r.status === 200 })) {
      joinFailed.add(1);
      return;
    }
  }

  const joinedAt = Date.now();
  const deadline = joinedAt + holdSec * 1000;
  const state = { kicked: false, lastPollAt: joinedAt, maxGap: 0 };

  const poll = () => {
    if (state.kicked) return;
    if (DROP_RATE > 0 && Math.random() < DROP_RATE) return; // 이번 조회는 놓쳤다
    const res = getRoom(me.token, roomId);
    pollDuration.add(res.timings.duration);
    const now = Date.now();
    state.maxGap = Math.max(state.maxGap, (now - state.lastPollAt) / 1000);
    if (res.status === 200) {
      state.lastPollAt = now;
      if (!isStillInRoom(res, me.userId)) {
        state.kicked = true;
        falseKicks.add(1, { slot: slot === 0 ? 'host' : 'guest' });
        kickAfter.add((now - joinedAt) / 1000);
      }
    }
  };

  poll(); // 입장 직후 한 번 조회 (Unity도 subscribed 직후 GET)

  if (USE_WS) {
    const url = `${WS_URL}?token=${encodeURIComponent(me.token)}`;
    ws.connect(url, { tags: { name: 'WS /ws/v1' } }, (socket) => {
      socket.on('open', () => socket.send(JSON.stringify({ type: 'room.subscribe', roomId })));
      socket.setInterval(() => socket.send(JSON.stringify({ type: 'ping' })), PING_SEC * 1000);
      socket.setInterval(() => {
        poll();
        if (state.kicked || Date.now() >= deadline) socket.close();
      }, POLL_SEC * 1000);
      // room.changed 신호가 오면 Unity는 바로 GET 하지만, 여기서는 "15초 안전망 폴링만으로 버티는가"를 보려고 무시한다.
      socket.setTimeout(() => socket.close(), holdSec * 1000 + 5000);
    });
  } else {
    // 예전 클라이언트: 1초 폴링
    while (!state.kicked && Date.now() < deadline) {
      sleep(1);
      poll();
    }
  }

  pollGapMax.add(state.maxGap);
  stayedRate.add(!state.kicked);
  if (!state.kicked) leaveRoom(me.token, roomId);
}

// ---------------------------------------------------------------------------
// 배경 부하: 로비에서 방 목록 새로고침
// ---------------------------------------------------------------------------
let browser = null; // VU마다 따로 생기는 모듈 변수
export function lobbyBrowser(data) {
  if (!browser) browser = guestLogin(`lt-${data.runId}-b${__VU}`); // VU마다 한 번만 로그인
  const res = listRooms(browser.token);
  check(res, { 'room list 200': (r) => r.status === 200 });
  sleep(randomBetween(BROWSE_SEC * 0.5, BROWSE_SEC * 1.5));
}

export function handleSummary(data) {
  const name = __ENV.OUT || `../results/s1_${Date.now()}.json`;
  return { [name]: JSON.stringify(data, null, 2), stdout: textSummary(data) };
}

function textSummary(data) {
  const m = data.metrics;
  const v = (k, f = 'count') => (m[k] && m[k].values[f] !== undefined ? m[k].values[f] : 0);
  return [
    '',
    '===== 시나리오 1: 대기실 튕김 =====',
    `방 ${ROOMS} × ${PLAYERS}명, HOLD ${HOLD}, POLL ${POLL_SEC}s, DROP ${DROP_RATE}, WS ${USE_WS}, BROWSERS ${BROWSERS}`,
    `억울한 퇴장(false kick): ${v('lobby_false_kicks')}`,
    `끝까지 남은 비율: ${(v('lobby_stayed', 'rate') * 100).toFixed(1)}%`,
    `입장 실패: ${v('lobby_join_failed')}`,
    `대기실 조회 p95/p99/max(ms): ${v('lobby_poll_duration', 'p(95)').toFixed(0)} / ${v('lobby_poll_duration', 'p(99)').toFixed(0)} / ${v('lobby_poll_duration', 'max').toFixed(0)}`,
    `참가자별 최대 조회 간격 p95/max(s): ${v('lobby_poll_gap_max_s', 'p(95)').toFixed(1)} / ${v('lobby_poll_gap_max_s', 'max').toFixed(1)}`,
    `HTTP 실패율: ${(v('http_req_failed', 'rate') * 100).toFixed(2)}%`,
    '',
  ].join('\n');
}

function parseDuration(s) {
  const mm = String(s).match(/^(\d+)(s|m|h)?$/);
  if (!mm) return 300;
  const n = Number(mm[1]);
  return mm[2] === 'h' ? n * 3600 : mm[2] === 'm' ? n * 60 : n;
}
