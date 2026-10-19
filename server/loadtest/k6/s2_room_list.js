/**
 * 시나리오 2 — 방 목록 조회(GET /api/v1/rooms)의 N+1 Redis 호출
 *
 * 구조: SMEMBERS lobby 방 ID 목록 → 방마다 GET room:{id} (방 N개면 Redis 왕복 N+1번, 직렬)
 * 측정: 방 개수(ROOMS)를 바꿔 가며 같은 동시 사용자로 목록 조회 p95/RPS가 어떻게 변하는지.
 *
 * ⚠ 서버를 대기실 연결 끊김 처리 OFF로 띄워야 한다. (방장이 조회를 안 하므로 45초 뒤 방이 지워짐)
 *     MAFIA_LOBBY_IDLE_TIMEOUT_SECONDS=0 ./gradlew bootRun
 *
 * 실행 예 (방 개수만 바꿔 3번)
 *   k6 run -e ROOMS=100  s2_room_list.js
 *   k6 run -e ROOMS=500  s2_room_list.js
 *   k6 run -e ROOMS=1000 s2_room_list.js
 *   검색도 같이: -e KEYWORD=부하
 */
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Trend } from 'k6/metrics';
import { BASE_URL, authHeaders, guestLogin, listRooms } from './lib/common.js';

const ROOMS = Number(__ENV.ROOMS || 100);
const PEAK_VUS = Number(__ENV.VUS || 100);
const THINK_SEC = Number(__ENV.THINK_SEC || 1);     // 새로고침 간격
const KEYWORD = __ENV.KEYWORD || '';

const listDuration = new Trend('room_list_duration', true);
const listSize = new Trend('room_list_size');

export const options = {
  setupTimeout: '15m',
  scenarios: {
    browse: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: Math.round(PEAK_VUS / 4) },
        { duration: '1m', target: Math.round(PEAK_VUS / 4) },
        { duration: '30s', target: Math.round(PEAK_VUS / 2) },
        { duration: '1m', target: Math.round(PEAK_VUS / 2) },
        { duration: '30s', target: PEAK_VUS },
        { duration: '2m', target: PEAK_VUS },
        { duration: '20s', target: 0 },
      ],
      gracefulRampDown: '10s',
    },
  },
  thresholds: { room_list_duration: ['p(95)<300'] },
  summaryTrendStats: ['avg', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

export function setup() {
  const runId = `${Date.now()}`;
  // 이미 있는 방은 그대로 두고, 모자란 만큼만 만든다
  const viewer = guestLogin(`lt-${runId}-seed-viewer`);
  const existing = (listRooms(viewer.token).json() || []).length;
  const need = Math.max(0, ROOMS - existing);
  const CHUNK = 50;
  for (let start = 0; start < need; start += CHUNK) {
    const idx = [...Array(Math.min(CHUNK, need - start)).keys()].map((i) => start + i);
    const logins = http.batch(idx.map((i) => ['POST', `${BASE_URL}/api/members/guest`,
      JSON.stringify({ guestUuid: `lt-${runId}-seed${i}` }),
      { headers: { 'Content-Type': 'application/json' }, tags: { name: 'setup login' } }]));
    http.batch(logins.map((r, k) => ['POST', `${BASE_URL}/api/v1/rooms`,
      JSON.stringify({ title: `부하 ${idx[k] % 7 === 0 ? '초보만' : '아무나'} ${idx[k]}`, maxPlayers: 8, privateRoom: false }),
      { ...authHeaders(r.json('accessToken')), tags: { name: 'setup create room' } }]));
  }
  const total = (listRooms(viewer.token).json() || []).length;
  console.log(`방 준비 완료: 기존 ${existing} + 새로 ${need} → 현재 ${total}개`);
  return { runId };
}

let me = null;
export default function (data) {
  if (!me) me = guestLogin(`lt-${data.runId}-v${__VU}`);
  const res = listRooms(me.token, KEYWORD);
  const ok = check(res, { 'list 200': (r) => r.status === 200 });
  listDuration.add(res.timings.duration);
  if (ok) listSize.add((res.json() || []).length);
  sleep(THINK_SEC);
}

export function handleSummary(data) {
  const name = __ENV.OUT || `../results/s2_rooms${ROOMS}_${Date.now()}.json`;
  const m = data.metrics;
  const v = (k, f) => (m[k] && m[k].values[f] !== undefined ? m[k].values[f] : 0);
  const text = [
    '',
    `===== 시나리오 2: 방 목록 (방 ${ROOMS}개, 최대 ${PEAK_VUS} VU${KEYWORD ? `, 검색 "${KEYWORD}"` : ''}) =====`,
    `목록 조회 avg/p95/p99(ms): ${v('room_list_duration', 'avg').toFixed(0)} / ${v('room_list_duration', 'p(95)').toFixed(0)} / ${v('room_list_duration', 'p(99)').toFixed(0)}`,
    `처리량(req/s): ${v('http_reqs', 'rate').toFixed(1)}`,
    `HTTP 실패율: ${(v('http_req_failed', 'rate') * 100).toFixed(2)}%`,
    '',
  ].join('\n');
  return { [name]: JSON.stringify(data, null, 2), stdout: text };
}
