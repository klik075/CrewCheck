// game-chat 부하테스트 공용 함수 (prepare.js, game-chat.js가 같이 쓴다)
import http from 'k6/http';
import { check, fail } from 'k6';

export const BASE_URL = (__ENV.BASE_URL || 'http://localhost:8080').replace(/\/$/, '');

export function authHeaders(token) {
  return { headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` } };
}

/** 게스트 로그인 여러 건을 한 번에 보낸다. 같은 guestUuid면 같은 계정으로 다시 로그인된다 */
export function guestLoginBatch(uuids) {
  const responses = http.batch(uuids.map((uuid) => ({
    method: 'POST',
    url: `${BASE_URL}/api/members/guest`,
    body: JSON.stringify({ guestUuid: uuid }),
    params: { headers: { 'Content-Type': 'application/json' }, tags: { name: 'guest_login' } },
  })));
  return responses.map((res, i) => {
    if (!check(res, { 'guest login 200': (r) => r.status === 200 })) {
      fail(`게스트 로그인 실패 uuid=${uuids[i]} status=${res.status} body=${res.body}`);
    }
    const body = res.json();
    return { uuid: uuids[i], token: body.accessToken, userId: body.userId, memberId: body.memberId };
  });
}
