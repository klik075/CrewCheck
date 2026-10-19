// 부하 테스트 0단계: k6와 서버가 연결되는지 확인하는 스모크 테스트
// 실행: cd loadtest  →  k6 run smoke.js
// 서버 주소를 바꾸려면: k6 run -e BASE_URL=http://localhost:8080 smoke.js
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

export const options = {
  vus: 2,          // 가상 사용자 2명
  duration: '5s',  // 5초 동안 반복
  thresholds: {
    checks: ['rate==1.0'],             // 모든 확인이 통과해야 성공
    http_req_failed: ['rate==0'],      // 실패한 요청이 없어야 성공
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/actuator/health`);
  check(res, {
    '200 OK': (r) => r.status === 200,
    'status UP': (r) => r.json('status') === 'UP',
  });
}
