// 2단계 확인: VOTE 페이즈의 게임이 제대로 준비되는지 본다. (부하는 걸지 않는다)
// 실행: k6 run prepare-check.js            (방 1개)
//       k6 run -e ROOMS=5 prepare-check.js  (방 5개)
import http from 'k6/http';
import { check } from 'k6';
import { ROOMS, PLAYERS_PER_ROOM, BASE_URL } from './lib/config.js';
import { prepareRooms, authParams } from './lib/setup.js';

export const options = {
  vus: 1,
  iterations: 1,
  setupTimeout: '180s',
  // http.batch로 한 번에 보낼 수 있는 요청 수 (기본 10/6이면 방 수가 늘수록 준비가 느려진다)
  batch: 200,
  batchPerHost: 200,
  thresholds: { checks: ['rate==1.0'] },
};

export function setup() {
  return { rooms: prepareRooms(ROOMS, PLAYERS_PER_ROOM) };
}

export default function (data) {
  for (const room of data.rooms) {
    const host = room.players[0];
    const game = http.get(`${BASE_URL}/api/v1/games/${room.gameId}`, authParams(host.token, 'check: game state'));
    const detail = http.get(`${BASE_URL}/api/v1/rooms/${room.roomId}`, authParams(host.token, 'check: room'));
    check(game, { 'phase가 VOTE': (r) => r.json('phase') === 'VOTE' });
    check(detail, {
      '방이 IN_GAME': (r) => r.json('status') === 'IN_GAME',
      [`방 인원 ${PLAYERS_PER_ROOM}명 (채팅 가능)`]: (r) => (r.json('players') || []).length === PLAYERS_PER_ROOM,
    });
    console.log(`room ${room.roomId} / game ${room.gameId} / phase ${game.json('phase')} / players ${(detail.json('players') || []).length}`);
  }
}
