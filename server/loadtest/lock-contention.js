// 4단계 본 시나리오: 투표 마감 직전 동시 투표 + 채팅 폭주 + 상태 재조회를 방 N개에서 동시에 돌리고,
// 게임 잠금과 상관없는 로비·로그인(카나리아)이 같이 느려지는지 본다.
//
// 실행 (PowerShell, loadtest 폴더):
//   k6 run -e ROOMS=10 lock-contention.js
//   k6 run -e ROOMS=40 -e DURATION=2m lock-contention.js
// 매 실행 전: Redis 비우기 + 서버 재시작 (남은 게임이 측정에 섞이지 않게)
//
// 시나리오 4개 (모두 arrival-rate = "서버가 느려져도 정해진 속도로 계속 보낸다")
//  - vote_burst    : 방마다 VOTE_EVERY초에 한 번, 8명이 동시에 투표(confirm=false, 대상 바꾸기). 마감 직전 몰림을 흉내 낸다.
//                    confirm=false라 페이즈가 끝나지 않아 VOTE(300초) 동안 계속 걸 수 있다.
//  - chat_flood    : 1인당 초당 CHAT_RATE개 채팅. 매번 다른 문장이라 도배 차단(429)에 걸리지 않는다.
//  - state_refetch : 1인당 초당 STATE_RATE번 게임 상태 조회. 실시간 신호를 받고 다시 조회하거나 폴링으로 돌아간 클라이언트.
//  - canary        : 초당 CANARY_RATE번 로비 방 목록·게스트 로그인 (게임 잠금을 전혀 잡지 않는다 = 대조군)
//
// 닫힌 모델(VU가 응답을 기다린 뒤 다음 요청)을 쓰지 않는 이유: 서버가 느려지면 요청도 같이 줄어서 고갈이 가려진다.
// k6가 속도를 못 맞추면 결과의 dropped_iterations가 늘어난다. 그 경우 MAX_VUS를 올린다.
import http from 'k6/http';
import exec from 'k6/execution';
import { Counter } from 'k6/metrics';
import { BASE_URL, ROOMS, PLAYERS_PER_ROOM } from './lib/config.js';
import { prepareRooms } from './lib/setup.js';

const DURATION = __ENV.DURATION || '2m';
const VOTE_EVERY = parseFloat(__ENV.VOTE_EVERY || '2');    // 방 하나가 몇 초마다 동시 투표를 하나
const CHAT_RATE = parseFloat(__ENV.CHAT_RATE || '1');      // 1인당 초당 채팅 수
const STATE_RATE = parseFloat(__ENV.STATE_RATE || '1');    // 1인당 초당 상태 조회 수
const CANARY_RATE = parseInt(__ENV.CANARY_RATE || '5', 10);
const MAX_VUS = parseInt(__ENV.MAX_VUS || '2000', 10);

const players = ROOMS * PLAYERS_PER_ROOM;
// arrival-rate는 정수 rate만 받으므로 "timeUnit당 몇 번"으로 바꾼다 (예: 방 10개가 2초마다 → 10번/2초)
const perSecond = (r) => ({ rate: Math.max(1, Math.round(r * 10)), timeUnit: '10s' });

function scenario(execName, rateSpec, startTime) {
  return Object.assign({
    executor: 'constant-arrival-rate',
    exec: execName,
    duration: DURATION,
    startTime,
    preAllocatedVUs: 50,
    maxVUs: MAX_VUS,
  }, rateSpec);
}

export const options = {
  setupTimeout: '180s',
  batch: 200,
  batchPerHost: 200,
  scenarios: {
    vote_burst: scenario('voteBurst', { rate: ROOMS, timeUnit: `${VOTE_EVERY}s` }, '0s'),
    chat_flood: scenario('chatFlood', perSecond(players * CHAT_RATE), '0s'),
    state_refetch: scenario('stateRefetch', perSecond(players * STATE_RATE), '0s'),
    canary: scenario('canary', { rate: CANARY_RATE, timeUnit: '1s' }, '0s'),
  },
  // 결과 요약에 요청 종류별 응답시간이 따로 나오게 하려고 항상 통과하는 기준을 건다 (판정용이 아니다)
  thresholds: {
    'http_req_duration{name:vote}': ['p(99)>=0'],
    'http_req_duration{name:chat send}': ['p(99)>=0'],
    'http_req_duration{name:game state}': ['p(99)>=0'],
    'http_req_duration{name:canary rooms}': ['p(99)>=0'],
    'http_req_duration{name:canary login}': ['p(99)>=0'],
    'status_503_lock_busy': ['count>=0'],
    'status_429_spam': ['count>=0'],
    'status_other_error': ['count>=0'],
  },
};

const lockBusy = new Counter('status_503_lock_busy');
const spam = new Counter('status_429_spam');
const otherError = new Counter('status_other_error');

// 503·429도 "예상한 응답"으로 둬서 http_req_failed에 섞이지 않게 하고, 위 카운터로 따로 센다
const expected = http.expectedStatuses({ min: 200, max: 299 }, 429, 503);

function params(token, name) {
  return {
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    tags: { name },
    responseCallback: expected,
  };
}

function count(res) {
  if (res.status === 503) lockBusy.add(1);
  else if (res.status === 429) spam.add(1);
  else if (res.status === 0 || res.status >= 400) otherError.add(1);
}

export function setup() {
  const rooms = prepareRooms(ROOMS, PLAYERS_PER_ROOM);
  // 카나리아용 로그인 사용자 1명 (방 목록 조회에 토큰이 필요하다)
  const canaryUser = http.post(`${BASE_URL}/api/members/guest`,
    JSON.stringify({ guestUuid: `canary-${Date.now()}` }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'setup: guest login', phase: 'setup' } });
  console.log(`[run] 방 ${ROOMS}개, ${DURATION} 동안: 투표 ${VOTE_EVERY}초마다 8명 동시, 채팅 1인당 ${CHAT_RATE}/s, 상태 조회 1인당 ${STATE_RATE}/s, 카나리아 ${CANARY_RATE}/s`);
  return { rooms, canaryToken: canaryUser.json('accessToken'), startedAt: Date.now() };
}

// ---------- 끝난 뒤: 서버 쪽 지표를 Prometheus에서 읽어 한 번에 출력 ----------
// Grafana에서 직접 읽지 않아도 표에 넣을 서버 쪽 숫자(바쁜 스레드, 잠금 대기·보유, timeout)가 나온다.
// 응답시간 p99(카나리아·투표)와 503 횟수는 k6 결과 요약에서 읽는다.
//  (서버가 재는 응답시간은 Tomcat 대기열에서 기다린 시간을 포함하지 않는다. 스레드가 바닥났을 때의 진짜 체감 시간은 k6 쪽이 맞다)
const PROM_URL = __ENV.PROM_URL || 'http://localhost:9090';

function promMax(query) {
  const res = http.get(`${PROM_URL}/api/v1/query?query=${encodeURIComponent(query)}`,
    { tags: { name: 'teardown: prometheus', phase: 'setup' }, responseCallback: http.expectedStatuses(200) });
  if (res.status !== 200) return null;
  const result = res.json('data.result');
  if (!result || result.length === 0) return null;
  const v = parseFloat(result[0].value[1]);
  return isNaN(v) ? null : v;
}

export function teardown(data) {
  // 부하 구간(준비가 끝난 뒤 ~ 지금)만 본다. Prometheus가 마지막 몇 초를 아직 긁지 않았을 수 있어 여유를 둔다
  const range = Math.max(30, Math.ceil((Date.now() - data.startedAt) / 1000) + 5);
  const sub = `[${range}s:1s]`;
  const busy = promMax(`max_over_time(sum(tomcat_threads_busy_threads)${sub})`);
  const maxThreads = promMax('max(tomcat_threads_config_max_threads)');
  const wait = promMax(`max_over_time(histogram_quantile(0.99, sum by (le) (rate(game_lock_wait_seconds_bucket{result="acquired"}[15s])))${sub})`);
  const hold = promMax(`max_over_time(histogram_quantile(0.99, sum by (le) (rate(game_lock_hold_seconds_bucket[15s])))${sub})`);
  const timeouts = promMax(`sum(increase(game_lock_wait_seconds_count{result="timeout"}[${range}s]))`);
  const ms = (v) => (v === null ? '없음' : `${(v * 1000).toFixed(0)}ms`);
  const n = (v) => (v === null ? '없음' : `${Math.round(v)}`);
  console.log([
    '',
    `========== 서버 쪽 결과 (방 ${ROOMS}개, 부하 구간 ${range}초) ==========`,
    `바쁜 스레드 최대   : ${n(busy)} / ${n(maxThreads)}`,
    `잠금 대기 p99 최대 : ${ms(wait)}`,
    `잠금 보유 p99 최대 : ${ms(hold)}`,
    `잠금 timeout 횟수  : ${n(timeouts)}`,
    '(카나리아·투표 p99와 503 횟수는 아래 k6 결과 요약에서 읽는다)',
    '',
  ].join('\n'));
}

function pickRoom(data) {
  return data.rooms[exec.scenario.iterationInTest % data.rooms.length];
}

function pickPlayer(data) {
  const i = exec.scenario.iterationInTest % players;
  const room = data.rooms[Math.floor(i / PLAYERS_PER_ROOM)];
  return { room, player: room.players[i % PLAYERS_PER_ROOM] };
}

// 방 하나의 8명이 같은 순간에 투표한다. 대상은 매번 바꾼다(자기 자신 제외).
export function voteBurst(data) {
  const room = pickRoom(data);
  const n = room.players.length;
  const shift = 1 + (exec.scenario.iterationInTest % (n - 1));
  const res = http.batch(room.players.map((p, k) => ({
    method: 'POST',
    url: `${BASE_URL}/api/v1/games/${room.gameId}/votes`,
    body: JSON.stringify({ targetId: room.players[(k + shift) % n].userId, confirm: false }),
    params: params(p.token, 'vote'),
  })));
  res.forEach(count);
}

export function chatFlood(data) {
  const { room, player } = pickPlayer(data);
  const res = http.post(`${BASE_URL}/api/v1/rooms/${room.roomId}/messages`,
    JSON.stringify({ message: `부하 테스트 메시지 ${exec.scenario.iterationInTest}` }),
    params(player.token, 'chat send'));
  count(res);
}

export function stateRefetch(data) {
  const { room, player } = pickPlayer(data);
  count(http.get(`${BASE_URL}/api/v1/games/${room.gameId}`, params(player.token, 'game state')));
}

// 대조군: 게임 잠금을 잡지 않는 API. 이게 느려지면 문제가 다른 API로 번진 것이다
export function canary(data) {
  if (exec.scenario.iterationInTest % 2 === 0) {
    count(http.get(`${BASE_URL}/api/v1/rooms`, params(data.canaryToken, 'canary rooms')));
  } else {
    const res = http.post(`${BASE_URL}/api/members/guest`,
      JSON.stringify({ guestUuid: `canary-${exec.scenario.iterationInTest}-${Date.now()}` }),
      { headers: { 'Content-Type': 'application/json' }, tags: { name: 'canary login' }, responseCallback: expected });
    count(res);
  }
}
