// 1단계: 게임을 시작하기 직전 상태의 방을 만들어 fixture.json에 저장한다.
//   k6 run -e ROOMS=20 -e PLAYERS=8 prepare.js
// 방마다: 게스트 PLAYERS명 로그인 → 방장이 방 생성 → 나머지 입장 → 방장이 직업 구성(CUSTOM) 저장 → 나머지 준비 완료
// 게임 시작은 game-chat.js의 setup이 한다. (시작부터 1초 폴링이 붙어야 연결 끊김 처리에 걸리지 않으므로)
import http from 'k6/http';
import { check, fail } from 'k6';
import { BASE_URL, authHeaders, guestLoginBatch } from './common.js';

const ROOMS = parseInt(__ENV.ROOMS || '20', 10);
const PLAYERS = parseInt(__ENV.PLAYERS || '8', 10);
const RUN_ID = __ENV.RUN_ID || `game${Date.now().toString(36)}`;
// 기본 구성: 해적 2 + 세이렌 1 + 선원 나머지 → 해적 밤 채팅·세이렌 팀 채팅·(처형 뒤) 사망자 채팅이 모두 나온다
// 바꾸려면 -e ROLES=PIRATE_RAIDER,PIRATE_RAIDER,CREW_SAILOR,... (개수 = PLAYERS)
const ROLES = (__ENV.ROLES || defaultRoles(PLAYERS)).split(',').map((s) => s.trim());

function defaultRoles(n) {
  const roles = ['PIRATE_RAIDER', 'PIRATE_RAIDER', 'NEUTRAL_SIREN'];
  while (roles.length < n) roles.push('CREW_SAILOR');
  return roles.join(',');
}

export const options = { setupTimeout: '10m', vus: 1, iterations: 1 };

function ok(res, name, expected) {
  if (!check(res, { [`${name} ${expected}`]: (r) => r.status === expected })) {
    fail(`${name} 실패 status=${res.status} body=${res.body}`);
  }
}

export function setup() {
  if (PLAYERS < 6 || PLAYERS > 12) fail('PLAYERS는 6~12 사이로 둔다 (해적 2 + 세이렌 1 + 선원 3 이상)');
  if (ROLES.length !== PLAYERS) fail(`ROLES 개수(${ROLES.length})가 PLAYERS(${PLAYERS})와 다릅니다`);
  const rooms = [];
  for (let r = 0; r < ROOMS; r++) {
    const uuids = [];
    for (let p = 0; p < PLAYERS; p++) uuids.push(`${RUN_ID}-${r}-${p}`);
    const players = guestLoginBatch(uuids);
    const host = players[0];

    const created = http.post(`${BASE_URL}/api/v1/rooms`,
      JSON.stringify({ title: `lt-${RUN_ID}-${r}`, maxPlayers: PLAYERS }), authHeaders(host.token));
    ok(created, 'create room', 201);
    const roomId = created.json('id');

    http.batch(players.slice(1).map((p) => ({
      method: 'POST', url: `${BASE_URL}/api/v1/rooms/${roomId}/players`, body: null, params: authHeaders(p.token),
    }))).forEach((res) => ok(res, 'join room', 200));

    ok(http.put(`${BASE_URL}/api/v1/rooms/${roomId}/role-setup`,
      JSON.stringify({ mode: 'CUSTOM', customCompositions: [{ playerCount: PLAYERS, roles: ROLES }], randomCandidates: null }),
      authHeaders(host.token)), 'role setup', 204);

    http.batch(players.slice(1).map((p) => ({
      method: 'PUT', url: `${BASE_URL}/api/v1/rooms/${roomId}/players/me/ready`,
      body: JSON.stringify({ ready: true }), params: authHeaders(p.token),
    }))).forEach((res) => ok(res, 'ready', 204));

    rooms.push({ roomId, players: players.map((p) => ({ token: p.token, userId: p.userId })) });
  }
  console.log(`준비 완료: RUN_ID=${RUN_ID}, 방 ${rooms.length}개 x ${PLAYERS}명, 구성=${ROLES.join(',')}`);
  return { runId: RUN_ID, baseUrl: BASE_URL, players: PLAYERS, roles: ROLES, createdAt: new Date().toISOString(), rooms };
}

export default function () {}

export function handleSummary(data) {
  return {
    'fixture.json': JSON.stringify(data.setup_data, null, 2),
    stdout: `\nfixture.json 저장: RUN_ID=${data.setup_data.runId}, rooms=${data.setup_data.rooms.length}\n` +
      '이 fixture로 game-chat.js를 한 번 돌리면 게임이 시작된다. 다시 돌릴 때는 prepare.js부터 새로 실행하세요.\n',
  };
}
