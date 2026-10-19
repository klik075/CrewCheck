// 부하 테스트용 게임 준비: 실제 방 흐름을 거쳐 VOTE 페이즈에 와 있는 게임을 방 N개만큼 만든다.
//
//   1. 게스트 로그인        POST /api/members/guest                     {guestUuid}
//   2. 방 생성 (방장)        POST /api/v1/rooms                          {title, maxPlayers}
//   3. 나머지 입장           POST /api/v1/rooms/{roomId}/players          (공개방이라 body 없음)
//   4. 나머지 준비           PUT  /api/v1/rooms/{roomId}/players/me/ready {ready:true}   ← 전원 준비가 시작 조건
//   5. 게임 시작 (방장)      POST /api/v1/rooms/{roomId}/games            → gameId
//   6. 전원 밤 능력 넘기기   POST /api/v1/games/{gameId}/night-actions/skip
//      능력을 쓸 수 있는 사람이 모두 넘기면 밤이 바로 판정된다. 능력이 없는 직업은 4xx가 나는데 정상이다.
//   7. VOTE 도착 확인        GET  /api/v1/games/{gameId}  (밤 결과 공개 5초 + 낮 5초 뒤. local 프로필 기준)
//
// 단계마다 모든 방을 http.batch로 한꺼번에 보낸다. 방을 하나씩 준비하면 방 40개에 10분 가까이 걸리고,
// 먼저 준비된 방은 VOTE 시간(300초)을 그만큼 써 버린다. 한꺼번에 하면 방 수와 상관없이 20초 안팎이다.
//
// 대기실 미접속 정리(45초)는 WAITING 방에만 적용되고, 입장할 때 접속 기록이 남으므로 이 흐름 안에서는 빠지지 않는다.
import http from 'k6/http';
import { sleep } from 'k6';
import { BASE_URL } from './config.js';

// 준비 단계 요청은 이 태그로 묶어서, 본 시나리오 지표와 섞여 보이지 않게 한다
const SETUP_TAG = { phase: 'setup' };

export function authParams(token, name) {
  return {
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    tags: Object.assign({ name }, SETUP_TAG),
  };
}

function send(requests) {
  return http.batch(requests);
}

function fail(what, res) {
  throw new Error(`${what} 실패: HTTP ${res.status} ${res.body}`);
}

function uuid() {
  // k6에는 crypto.randomUUID가 없어서 충분히 겹치지 않는 문자열로 만든다
  return `lt-${Date.now()}-${Math.random().toString(36).slice(2)}-${Math.random().toString(36).slice(2)}`;
}

/**
 * VOTE 페이즈의 게임을 roomCount개 만든다.
 * @returns [{ roomId, gameId, players: [{ userId, token }] }]   players[0]이 방장
 */
export function prepareRooms(roomCount, playersPerRoom) {
  const started = Date.now();
  const total = roomCount * playersPerRoom;
  console.log(`[setup] 방 ${roomCount}개 × ${playersPerRoom}명 = 게스트 ${total}명 준비 시작`);

  // 1. 게스트 로그인
  const loginRes = send(Array.from({ length: total }, () => ({
    method: 'POST',
    url: `${BASE_URL}/api/members/guest`,
    body: JSON.stringify({ guestUuid: uuid() }),
    params: { headers: { 'Content-Type': 'application/json' }, tags: Object.assign({ name: 'setup: guest login' }, SETUP_TAG) },
  })));
  const users = loginRes.map((res) => {
    if (res.status !== 200) fail('게스트 로그인', res);
    return { userId: res.json('userId'), token: res.json('accessToken') };
  });
  const rooms = Array.from({ length: roomCount }, (_, r) => ({
    roomId: null,
    gameId: null,
    players: users.slice(r * playersPerRoom, (r + 1) * playersPerRoom),
  }));

  // 2. 방 생성 (각 방의 첫 번째 사람이 방장)
  const createRes = send(rooms.map((room, r) => ({
    method: 'POST',
    url: `${BASE_URL}/api/v1/rooms`,
    body: JSON.stringify({ title: `loadtest-${r + 1}`, maxPlayers: playersPerRoom }),
    params: authParams(room.players[0].token, 'setup: create room'),
  })));
  createRes.forEach((res, r) => {
    if (res.status !== 201) fail('방 생성', res);
    rooms[r].roomId = res.json('id');
  });

  // 3·4. 방장을 뺀 나머지 입장 → 준비
  const guests = [];
  rooms.forEach((room) => room.players.slice(1).forEach((p) => guests.push({ room, p })));
  send(guests.map(({ room, p }) => ({
    method: 'POST',
    url: `${BASE_URL}/api/v1/rooms/${room.roomId}/players`,
    body: null,
    params: authParams(p.token, 'setup: join room'),
  }))).forEach((res) => { if (res.status !== 200) fail('방 입장', res); });
  send(guests.map(({ room, p }) => ({
    method: 'PUT',
    url: `${BASE_URL}/api/v1/rooms/${room.roomId}/players/me/ready`,
    body: JSON.stringify({ ready: true }),
    params: authParams(p.token, 'setup: ready'),
  }))).forEach((res) => { if (res.status !== 204) fail('준비', res); });

  // 5. 게임 시작 (방장)
  send(rooms.map((room) => ({
    method: 'POST',
    url: `${BASE_URL}/api/v1/rooms/${room.roomId}/games`,
    body: null,
    params: authParams(room.players[0].token, 'setup: start game'),
  }))).forEach((res, r) => {
    if (res.status !== 201) fail('게임 시작', res);
    rooms[r].gameId = res.json('gameId');
  });

  // 6. 전원 밤 능력 넘기기 (능력이 없는 직업은 4xx = 정상)
  const all = [];
  rooms.forEach((room) => room.players.forEach((p) => all.push({ room, p })));
  const skipRes = send(all.map(({ room, p }) => ({
    method: 'POST',
    url: `${BASE_URL}/api/v1/games/${room.gameId}/night-actions/skip`,
    body: null,
    // 능력이 없는 직업(선원)은 4xx로 거절된다. 정상이므로 실패로 세지 않는다
    params: Object.assign(authParams(p.token, 'setup: night skip'),
      { responseCallback: http.expectedStatuses({ min: 200, max: 499 }) }),
  })));
  skipRes.forEach((res) => { if (res.status >= 500) fail('밤 넘기기', res); });

  // 7. VOTE 도착 확인 (최대 40초)
  waitForPhase(rooms, 'VOTE', 40);

  console.log(`[setup] 방 ${roomCount}개 VOTE 도착 (${((Date.now() - started) / 1000).toFixed(1)}초)`);
  return rooms;
}

function waitForPhase(rooms, target, timeoutSeconds) {
  const deadline = Date.now() + timeoutSeconds * 1000;
  const firstSeen = Date.now();
  let pending = rooms.slice();
  while (pending.length > 0) {
    const res = send(pending.map((room) => ({
      method: 'GET',
      url: `${BASE_URL}/api/v1/games/${room.gameId}`,
      params: authParams(room.players[0].token, 'setup: wait phase'),
    })));
    const next = [];
    res.forEach((r, i) => {
      if (r.status !== 200) fail('게임 상태 조회', r);
      const phase = r.json('phase');
      if (phase === 'ENDED') throw new Error(`게임 ${pending[i].gameId}이 준비 중에 끝났습니다 (${r.json('endReason')})`);
      // 밤 넘기기 뒤에도 NIGHT에 오래 머물면 누군가 넘기지 못한 것이다 (local 프로필은 밤이 300초라 그대로 기다리면 멈춘 것처럼 보인다)
      if (phase === 'NIGHT' && Date.now() - firstSeen > 5000) {
        throw new Error(`게임 ${pending[i].gameId}이 아직 NIGHT입니다. 밤 넘기기가 일부 실패했는지 서버 로그를 확인하세요`);
      }
      if (phase !== target) next.push(pending[i]);
    });
    pending = next;
    if (pending.length === 0) break;
    if (Date.now() > deadline) throw new Error(`${timeoutSeconds}초 안에 ${target}에 오지 못한 게임 ${pending.length}개`);
    sleep(1);
  }
}
