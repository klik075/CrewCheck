// 부하 테스트 공통 설정. 실행할 때 -e 로 바꾼다.  예) k6 run -e ROOMS=10 prepare-check.js
export const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
export const ROOMS = parseInt(__ENV.ROOMS || '1', 10);              // 준비할 방(게임) 수
export const PLAYERS_PER_ROOM = parseInt(__ENV.PLAYERS || '8', 10);  // 방 하나의 인원 (4~12)
