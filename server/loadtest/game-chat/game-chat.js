// 2단계: 게임 중 채팅과 게임 잠금 경합.
//   k6 run -e CHAT_PER_SEC=0.5 game-chat.js
//
// VU 1개 = 플레이어 1명. 실제 게임 화면처럼 1초마다
//   ① GET /games/{id} (상태 폴링, 게임 잠금)
//   ② 투표 페이즈면 투표 (KILL_DAYS에 정한 날에만, 정해 둔 선원 1명에게 → 처형되어 사망자 채팅이 생긴다)
//   ③ CHAT_PER_SEC 확률로 채팅 전송 (게임 잠금 안에서 규칙·볼 사람 결정)
//   ④ GET /rooms/{id}/messages?afterId= (숨김 메시지가 있으면 게임 잠금)
// 을 한다. 채팅량만 바꿔 가며 같은 테스트를 돌려, 채팅이 게임 진행(페이즈 전환)과 폴링을 얼마나 늦추는지 본다.
//
// 환경 변수
//   BASE_URL       서버 주소 (기본 http://localhost:8080)
//   CHAT_PER_SEC   플레이어 1명이 1초에 보내는 채팅 수 (기본 0.5, 0이면 채팅 없는 기준선). 1보다 크면 1초에 여러 번
//   DURATION       테스트 시간 (기본 5m)
//   KILL_DAYS      투표로 선원을 처형할 날 (기본 "1,2"). 빈 값이면 아무도 죽이지 않는다
//   READ_CHAT      1(기본)이면 ④ 채팅 폴링도 한다
//   PHASE_NIGHT / PHASE_NIGHT_RESULT / PHASE_DAY / PHASE_VOTE / PHASE_EXECUTION
//                  서버에 준 mafia.phase.*-seconds와 같은 값 (기본 10 / 3 / 15 / 10 / 3). 페이즈 전환 지연 계산에 쓴다
import http from 'k6/http';
import { check, sleep, fail } from 'k6';
import { Trend, Counter, Rate } from 'k6/metrics';
import { BASE_URL, authHeaders } from './common.js';

const fixture = JSON.parse(open('./fixture.json'));

const CHAT_PER_SEC = parseFloat(__ENV.CHAT_PER_SEC || '0.5');
const DURATION = __ENV.DURATION || '5m';
const KILL_DAYS = (__ENV.KILL_DAYS === undefined ? '1,2' : __ENV.KILL_DAYS)
  .split(',').map((s) => parseInt(s, 10)).filter((n) => !isNaN(n));
const READ_CHAT = (__ENV.READ_CHAT || '1') === '1';
const PHASE_SECONDS = {
  NIGHT: parseInt(__ENV.PHASE_NIGHT || '10', 10),
  NIGHT_RESULT: parseInt(__ENV.PHASE_NIGHT_RESULT || '3', 10),
  DAY: parseInt(__ENV.PHASE_DAY || '15', 10),
  VOTE: parseInt(__ENV.PHASE_VOTE || '10', 10),
  EXECUTION: parseInt(__ENV.PHASE_EXECUTION || '3', 10),
};

// 방 x 참가자를 한 줄로 편다. VU 번호(1부터) - 1 = 이 배열의 위치
const SEATS = [];
fixture.rooms.forEach((room, r) => room.players.forEach((p, i) => SEATS.push({ r, i, roomId: room.roomId, userId: p.userId, token: p.token })));

// ---------- 지표 ----------
const gameStateMs = new Trend('game_state_ms', true);   // ① 상태 폴링 지연
const chatSendMs = new Trend('chat_send_ms', true);     // ③ 게임 중 채팅 전송 지연 (rule 태그: public / night_pirate / night_siren / dead)
const chatReadMs = new Trend('chat_read_ms', true);     // ④ 채팅 폴링 지연
const voteMs = new Trend('vote_ms', true);
const phaseLagMs = new Trend('phase_lag_ms', true);     // 페이즈가 예정 시각(phaseEndsAt)보다 늦게 바뀐 시간
const phaseEarly = new Counter('phase_early');          // 예정보다 일찍 바뀜 (전원 투표로 투표가 일찍 끝난 경우 등, 지연 계산에서 뺌)
const lockBusy = new Counter('lock_busy');              // 503 LOCK_BUSY (잠금 대기 시간 초과). endpoint 태그
const httpStatus = new Counter('game_http_status');     // endpoint·status 태그
const chatOk = new Rate('chat_send_ok');
const nightBlocked = new Counter('chat_night_blocked'); // 페이즈가 막 밤으로 바뀐 순간 보낸 일반 채팅 (403, 정상 동작)
const gamesEnded = new Counter('games_ended');

export const options = {
  setupTimeout: '5m',
  scenarios: {
    players: { executor: 'constant-vus', vus: SEATS.length, duration: DURATION, gracefulStop: '5s' },
  },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    game_state_ms: ['p(95)<300'],
    chat_send_ms: ['p(95)<500'],
    'chat_send_ms{rule:public}': ['p(95)<500'],
    'chat_send_ms{rule:night_pirate}': ['p(95)<500'],
    'chat_send_ms{rule:night_siren}': ['p(95)<500'],
    'chat_send_ms{rule:dead}': ['p(95)<500'],
    chat_read_ms: ['p(95)<300'],
    phase_lag_ms: ['p(95)<1000', 'max<3000'],
    lock_busy: ['count==0'],
    'game_http_status{status:5xx}': ['count==0'],
    games_ended: ['count==0'], // 테스트 도중 게임이 끝나면(취소·승리) 경합 조건이 바뀐다
  },
};

function tag(status) {
  if (status === 0) return 'network';
  if (status === 503) return '503';
  if (status >= 500) return '5xx';
  return String(status);
}

function record(res, endpoint) {
  httpStatus.add(1, { endpoint, status: tag(res.status) });
  if (res.status === 503) lockBusy.add(1, { endpoint });
}

// ---------- setup: 게임 시작 + 각자 직업 확인 ----------

function chunks(arr, size) {
  const out = [];
  for (let i = 0; i < arr.length; i += size) out.push(arr.slice(i, i + size));
  return out;
}

export function setup() {
  // 방장들이 동시에 게임 시작
  const starts = http.batch(fixture.rooms.map((room) => ({
    method: 'POST', url: `${BASE_URL}/api/v1/rooms/${room.roomId}/games`, body: null,
    params: authHeaders(room.players[0].token),
  })));
  const gameIds = starts.map((res, r) => {
    if (res.status !== 201) {
      fail(`게임 시작 실패 roomId=${fixture.rooms[r].roomId} status=${res.status} body=${res.body}\n` +
        '이미 게임을 한 번 돌린 fixture면 prepare.js를 다시 실행하세요.');
    }
    return res.json('gameId');
  });

  // 전원 내 직업 조회 (해적·세이렌인지 알아야 밤 채팅을 보낸다)
  const roles = {};
  chunks(SEATS, 100).forEach((part) => {
    http.batch(part.map((s) => ({
      method: 'GET', url: `${BASE_URL}/api/v1/games/${gameIds[s.r]}/me`, params: authHeaders(s.token),
    }))).forEach((res, k) => {
      const s = part[k];
      if (res.status !== 200) fail(`내 직업 조회 실패 userId=${s.userId} status=${res.status}`);
      roles[`${s.r}-${s.i}`] = res.json('role');
    });
  });

  // 방마다 처형할 선원(방장 제외)을 KILL_DAYS 순서대로 정한다
  const victims = fixture.rooms.map((room, r) => {
    const sailors = room.players
      .map((p, i) => ({ userId: p.userId, role: roles[`${r}-${i}`], i }))
      .filter((p) => p.i !== 0 && p.role === 'CREW_SAILOR')
      .map((p) => p.userId);
    if (sailors.length < KILL_DAYS.length) fail(`방 ${room.roomId}: 처형할 선원이 부족합니다 (KILL_DAYS를 줄이세요)`);
    return sailors.slice(0, KILL_DAYS.length);
  });

  console.log(`게임 ${gameIds.length}개 시작, 플레이어 ${SEATS.length}명, CHAT_PER_SEC=${CHAT_PER_SEC}, KILL_DAYS=${KILL_DAYS.join(',') || '없음'}`);
  return { gameIds, roles, victims, startedAt: Date.now() };
}

// ---------- 플레이어 1명 (VU마다 따로 갖는 상태) ----------

let last = null;       // 직전에 본 { phase, phaseVersion, phaseEndsAt }
let lastMsgId = null;  // 채팅 폴링 커서
let votedDay = -1;
let ended = false;

function ruleOf(phase, alive, role) {
  if (!alive) return 'dead';
  if (phase === 'NIGHT') {
    if (role && role.startsWith('PIRATE_')) return 'night_pirate';
    if (role === 'NEUTRAL_SIREN') return 'night_siren';
    return null; // 밤에는 일반 생존자는 채팅하지 않는다 (클라이언트도 입력을 막는다)
  }
  return 'public';
}

export default function (data) {
  const seat = SEATS[__VU - 1];
  if (!seat) { sleep(1); return; }
  const tickStart = Date.now();
  const gameId = data.gameIds[seat.r];
  const role = data.roles[`${seat.r}-${seat.i}`];
  const auth = authHeaders(seat.token);

  // ① 상태 폴링
  const stRes = http.get(`${BASE_URL}/api/v1/games/${gameId}`, Object.assign({ tags: { name: 'game_state' } }, auth));
  record(stRes, 'game_state');
  if (stRes.status !== 200) { sleep(1); return; }
  const st = stRes.json();
  gameStateMs.add(stRes.timings.duration, { phase: st.phase });

  if (st.phase === 'ENDED') {
    if (!ended && seat.i === 0) {
      gamesEnded.add(1, { reason: String(st.endReason) });
      console.warn(`게임 종료됨 gameId=${gameId} day=${st.day} endReason=${st.endReason} winner=${st.winner}`);
    }
    ended = true;
    sleep(1);
    return;
  }

  // 페이즈 전환 지연: 새 페이즈의 phaseEndsAt - 그 페이즈 길이 = 실제로 바뀐 시각. 이것과 직전 페이즈의 phaseEndsAt(예정 시각)의 차이
  // 방마다 방장 VU 하나만 기록한다. (폴링 주기와 상관없이 서버 시각 기준으로 정확하다)
  if (seat.i === 0 && last && st.phaseVersion !== last.phaseVersion && PHASE_SECONDS[st.phase] !== undefined) {
    const changedAt = Date.parse(st.phaseEndsAt) - PHASE_SECONDS[st.phase] * 1000;
    const lag = changedAt - Date.parse(last.phaseEndsAt);
    if (lag >= -200) {
      phaseLagMs.add(Math.max(0, lag), { from: last.phase, to: st.phase });
    } else {
      phaseEarly.add(1, { from: last.phase });
    }
  }
  last = { phase: st.phase, phaseVersion: st.phaseVersion, phaseEndsAt: st.phaseEndsAt };

  const me = st.players.find((p) => p.playerId === seat.userId);
  const alive = me ? me.alive : false;

  // ② 투표
  const killIndex = KILL_DAYS.indexOf(st.day);
  if (st.phase === 'VOTE' && alive && killIndex >= 0 && votedDay !== st.day) {
    const target = data.victims[seat.r][killIndex];
    if (target !== seat.userId) { // 처형될 사람은 투표하지 않는다
      const vRes = http.post(`${BASE_URL}/api/v1/games/${gameId}/votes`,
        JSON.stringify({ targetId: target, confirm: true }), Object.assign({ tags: { name: 'vote' } }, auth));
      record(vRes, 'vote');
      voteMs.add(vRes.timings.duration);
      check(vRes, { 'vote 2xx': (r) => r.status >= 200 && r.status < 300 });
    }
    votedDay = st.day;
  }

  // ③ 채팅 전송
  const rule = ruleOf(st.phase, alive, role);
  let sends = Math.floor(CHAT_PER_SEC) + (Math.random() < CHAT_PER_SEC % 1 ? 1 : 0);
  if (!rule) sends = 0;
  for (let k = 0; k < sends; k++) {
    const text = `${st.phase} ${st.day}일차 ${rule} 채팅 #${seat.userId}-${__ITER}-${k}`; // 매번 달라 도배 방지에 걸리지 않는다
    const cRes = http.post(`${BASE_URL}/api/v1/rooms/${seat.roomId}/messages`, JSON.stringify({ message: text }),
      Object.assign({ tags: { name: 'chat_send' } }, auth));
    record(cRes, 'chat_send');
    if (cRes.status === 403 && String(cRes.body).includes('밤에는')) {
      nightBlocked.add(1); // 폴링 직후 밤이 시작된 경우. 서버 규칙대로 막힌 것이라 오류로 보지 않는다
      break;
    }
    chatSendMs.add(cRes.timings.duration, { rule, phase: st.phase });
    chatOk.add(cRes.status === 201);
  }

  // ④ 채팅 폴링
  if (READ_CHAT) {
    const url = lastMsgId === null
      ? `${BASE_URL}/api/v1/rooms/${seat.roomId}/messages?limit=1`
      : `${BASE_URL}/api/v1/rooms/${seat.roomId}/messages?afterId=${lastMsgId}&limit=50`;
    const rRes = http.get(url, Object.assign({ tags: { name: 'chat_read' } }, auth));
    record(rRes, 'chat_read');
    if (rRes.status === 200) {
      chatReadMs.add(rRes.timings.duration, { alive: String(alive) });
      const list = rRes.json();
      if (list.length) lastMsgId = list[list.length - 1].messageId;
      else if (lastMsgId === null) lastMsgId = 0;
    }
  }

  const rest = 1000 - (Date.now() - tickStart);
  if (rest > 0) sleep(rest / 1000);
}

export function teardown(data) {
  // 게임마다 마지막 상태를 남긴다. 아직 진행 중이어야 정상 (테스트가 끝나면 폴링이 끊겨 60초 뒤 취소된다)
  const res = http.batch(data.gameIds.map((gid, r) => ({
    method: 'GET', url: `${BASE_URL}/api/v1/games/${gid}`, params: authHeaders(fixture.rooms[r].players[0].token),
  })));
  const summary = {};
  res.forEach((x) => {
    if (x.status !== 200) { summary[`status ${x.status}`] = (summary[`status ${x.status}`] || 0) + 1; return; }
    const s = x.json();
    const key = `${s.phase}${s.endReason ? `(${s.endReason})` : ''}`;
    summary[key] = (summary[key] || 0) + 1;
  });
  const elapsed = Math.round((Date.now() - data.startedAt) / 1000);
  console.log(`[teardown] ${elapsed}초 경과, 게임 상태 분포: ${JSON.stringify(summary)}`);
}
