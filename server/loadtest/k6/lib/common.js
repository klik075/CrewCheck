// 공통 헬퍼: 게스트 로그인, 방 API, 응답 파싱
// 모든 시나리오는 BASE_URL / WS_URL 환경변수로 대상 서버를 바꾼다.
import http from 'k6/http';
import { check, fail } from 'k6';

export const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
export const WS_URL = __ENV.WS_URL || BASE_URL.replace(/^http/, 'ws') + '/ws/v1';

const JSON_HEADERS = { 'Content-Type': 'application/json' };

export function authHeaders(token) {
  return { headers: { ...JSON_HEADERS, Authorization: `Bearer ${token}` } };
}

/**
 * 게스트 로그인 (POST /api/members/guest).
 * 같은 guestUuid면 같은 계정을 재사용하므로, 실행마다 runId를 섞어 새 계정을 만든다.
 * @returns {{token: string, userId: number, memberId: number}}
 */
export function guestLogin(guestUuid, tags = {}) {
  const res = http.post(`${BASE_URL}/api/members/guest`, JSON.stringify({ guestUuid }), {
    headers: JSON_HEADERS,
    tags: { name: 'POST /api/members/guest', ...tags },
  });
  if (!check(res, { 'guest login 200': (r) => r.status === 200 })) {
    fail(`guest login 실패 (${res.status}): ${res.body}`);
  }
  const body = res.json();
  return { token: body.accessToken, userId: body.userId, memberId: body.memberId };
}

export function createRoom(token, title, maxPlayers) {
  const res = http.post(
    `${BASE_URL}/api/v1/rooms`,
    JSON.stringify({ title, maxPlayers, privateRoom: false }),
    { ...authHeaders(token), tags: { name: 'POST /api/v1/rooms' } },
  );
  if (!check(res, { 'create room 201': (r) => r.status === 201 })) {
    fail(`방 생성 실패 (${res.status}): ${res.body}`);
  }
  return res.json().id;
}

export function joinRoom(token, roomId) {
  return http.post(`${BASE_URL}/api/v1/rooms/${roomId}/players`, null, {
    ...authHeaders(token),
    tags: { name: 'POST /api/v1/rooms/{id}/players' },
  });
}

export function getRoom(token, roomId) {
  return http.get(`${BASE_URL}/api/v1/rooms/${roomId}`, {
    ...authHeaders(token),
    tags: { name: 'GET /api/v1/rooms/{id}' },
    timeout: '20s',
  });
}

export function leaveRoom(token, roomId) {
  return http.del(`${BASE_URL}/api/v1/rooms/${roomId}/players/me`, null, {
    ...authHeaders(token),
    tags: { name: 'DELETE /api/v1/rooms/{id}/players/me' },
  });
}

export function listRooms(token, keyword) {
  const q = keyword ? `?keyword=${encodeURIComponent(keyword)}` : '';
  return http.get(`${BASE_URL}/api/v1/rooms${q}`, {
    ...authHeaders(token),
    tags: { name: keyword ? 'GET /api/v1/rooms?keyword' : 'GET /api/v1/rooms' },
    timeout: '30s',
  });
}

/** 방 상세 응답에 내가 참가자로 남아 있는지 */
export function isStillInRoom(res, userId) {
  try {
    return (res.json('players') || []).some((p) => p.userId === userId);
  } catch (e) {
    return false;
  }
}

export function randomBetween(min, max) {
  return min + Math.random() * (max - min);
}
