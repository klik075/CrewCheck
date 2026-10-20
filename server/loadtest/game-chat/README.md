# 게임 중 채팅과 게임 잠금 경합 (k6)

게임 중 채팅은 전송할 때마다 **게임 잠금**(`gameLock.withLock(gameId)`)을 잡는다. 규칙(사망자·밤 해적·세이렌)과
볼 사람(audience)을 잠금 안에서 정하기 때문이다. 채팅 조회도 숨김 메시지(사망자·해적·세이렌)가 있으면
`Visibility.resolve()`에서 같은 잠금을 잡는다. 이 잠금은 아래 작업들과 같은 것이다.

- 페이즈 전환 (타이머 → `GameFlowService.moveTo`)
- 투표, 밤 능력
- 상태 폴링 `GET /games/{id}` (1초마다, 플레이어 전원)

이 테스트는 **채팅량만 바꿔 가며 같은 게임을 돌려서**, 채팅이 늘 때 아래 세 가지가 얼마나 나빠지는지 본다.

1. 페이즈 전환이 예정 시각보다 늦어지는 정도 (`phase_lag_ms`) ← 핵심
2. 상태 폴링·투표 지연 (`game_state_ms`, `vote_ms`)
3. 잠금 대기 시간 초과 (`lock_busy` = 503 LOCK_BUSY, `mafia.lock.wait-timeout-millis`=10초)

## 시나리오

- 방마다 8명. 직업은 해적 2 + 세이렌 1 + 선원 5로 고정한다 (`prepare.js`, `-e ROLES=...`로 바꿀 수 있다)
- VU 1개 = 플레이어 1명이고, 1초마다 실제 게임 화면처럼 움직인다
  1. `GET /games/{id}`: 상태 폴링 (잠금)
  2. 투표 페이즈면 투표: `KILL_DAYS`(기본 1, 2일차)에만 정해 둔 선원 1명에게 → 처형 → **사망자 채팅이 생긴다**
  3. 채팅 전송, 확률 `CHAT_PER_SEC`: 낮은 공개 채팅, 밤은 해적·세이렌만, 사망자는 언제나 사망자 채팅 (잠금)
  4. `GET /rooms/{id}/messages?afterId=`: 채팅 폴링 (숨김 메시지가 있으면 잠금)
- 밤 능력은 쓰지 않는다. 그래서 밤에는 아무도 죽지 않고, 게임은 처형 2명 뒤로는 끝나지 않고 계속된다
- 페이즈 전환 지연은 서버 시각으로 계산한다: (새 페이즈 `phaseEndsAt` − 그 페이즈 길이) − 직전 페이즈 `phaseEndsAt`.
  서버는 전환할 때 `endsAt = now + 길이`로 정하므로, 늦게 전환되면 그만큼 값이 커진다.
  전원 투표로 투표가 일찍 끝난 경우는 `phase_early`로 따로 세고 지연 계산에서 뺀다.

## 0. 준비

1. k6 설치 (Windows: `winget install k6`), 프로젝트 루트에서 `docker compose up -d` (MySQL + Redis)
2. 서버 실행: **페이즈를 짧게** 해서 5분 안에 여러 날이 돌게 한다 (하루 41초)
   ```powershell
   $env:SERVER_MODE="multi"   # 게임 잠금 = Redisson 분산 잠금, 타이머 = Redis (운영과 같은 조건). 빼면 서버 메모리 잠금
   .\gradlew.bat bootRun --args="--mafia.phase.night-seconds=10 --mafia.phase.night-result-seconds=3 --mafia.phase.day-seconds=15 --mafia.phase.vote-seconds=10 --mafia.phase.execution-seconds=3"
   ```
   - 페이즈 길이를 바꾸면 k6에도 같은 값을 `-e PHASE_NIGHT=... PHASE_NIGHT_RESULT PHASE_DAY PHASE_VOTE PHASE_EXECUTION`으로 넘긴다 (지연 계산에 쓴다)
   - `max-days-without-death`(기본 10일)가 지나면 게임이 취소된다. 하루 41초면 처형 2명 뒤 약 8분까지 안전하다 → `DURATION`은 6분 이하로 둔다
   - 서버 로그 레벨은 INFO 이상, SQL 로그는 끈다

명령은 모두 `loadtest/game-chat` 폴더에서 실행한다. (공용 함수는 같은 폴더의 `common.js`)

## 1. 실행 — 채팅량만 바꿔 가며 4번

**게임은 한 번 시작하면 끝까지 가므로, 실행할 때마다 `prepare.js`부터 다시 한다.**

```powershell
# (A) 기준선: 채팅 없음
k6 run -e ROOMS=20 prepare.js
k6 run -e CHAT_PER_SEC=0 --summary-export result-A-chat0.json game-chat.js

# (B) 보통: 1명이 2초에 1번
k6 run -e ROOMS=20 prepare.js
k6 run -e CHAT_PER_SEC=0.5 --summary-export result-B-chat0.5.json game-chat.js

# (C) 많음: 1명이 1초에 2번
k6 run -e ROOMS=20 prepare.js
k6 run -e CHAT_PER_SEC=2 --summary-export result-C-chat2.json game-chat.js

# (D) 과부하: 1명이 1초에 5번
k6 run -e ROOMS=20 prepare.js
k6 run -e CHAT_PER_SEC=5 --summary-export result-D-chat5.json game-chat.js
```

방 20개 × 8명 = VU 160개다. 지연이 거의 늘지 않으면 `ROOMS`를 50 → 100으로 늘려 같은 4번을 반복한다.

### 원인 나누기 (선택)

| 실행 | 확인하는 것 |
|---|---|
| `-e READ_CHAT=0` | 채팅 조회(Visibility 잠금)를 빼고 **전송만**의 영향 |
| `-e KILL_DAYS=` (빈 값) | 사망자가 없어 숨김 메시지가 거의 없음 → 조회가 잠금을 안 잡는 경우와 비교 |
| `SERVER_MODE` 빼고 실행 | 서버 메모리 잠금 vs Redisson 잠금의 차이 |
| 멀티서버 `-e BASE_URL=http://localhost` | 서버 2대가 같은 게임의 잠금을 두고 경합 (nginx가 요청을 나눈다) |

## 2. 볼 지표 (k6 요약)

| 지표 | 의미 | 기준 (thresholds) |
|---|---|---|
| `phase_lag_ms` (`from`/`to` 태그) | 페이즈 전환이 예정보다 늦은 시간. 타이머 폴링 주기(100ms) + 잠금 대기 + 처리 | p95 < 1s, max < 3s |
| `game_state_ms` | 상태 폴링 지연 (잠금 대기가 그대로 보인다) | p95 < 300ms |
| `chat_send_ms` (`rule` 태그: public / night_pirate / night_siren / dead) | 게임 중 채팅 전송 지연. 규칙별로 나뉜다 | p95 < 500ms |
| `chat_read_ms` (`alive` 태그) | 채팅 폴링. 사망자(alive=false)는 숨김 메시지를 볼 수 있어 잠금을 잡는다 | p95 < 300ms |
| `vote_ms` | 투표 지연 | |
| `lock_busy` (`endpoint` 태그) | 503 LOCK_BUSY 건수 (10초 동안 잠금을 못 잡음) | 0 |
| `game_http_status{status:5xx}` | 서버 오류 | 0 |
| `games_ended` | 테스트 중에 게임이 끝남 (조건이 바뀌므로 다시 돌린다) | 0 |
| `chat_night_blocked` | 폴링 직후 밤이 시작돼 막힌 일반 채팅 (403, 정상 동작) | 참고용 |
| `phase_early` | 전원 투표로 투표가 일찍 끝난 횟수 | 참고용 |

`teardown` 로그에 게임별 마지막 페이즈 분포가 남는다. 전부 진행 중(NIGHT/DAY…)이어야 정상이다.

### 결과 정리 표

| 실행 | CHAT_PER_SEC | 채팅 rps (`chat_send_ms` count / 시간) | phase_lag p95 / max | game_state p95 | chat_send p95 (dead) | lock_busy |
|---|---|---|---|---|---|---|
| A | 0 | 0 | | | - | |
| B | 0.5 | ≈ 방×8×0.5 | | | | |
| C | 2 | | | | | |
| D | 5 | | | | | |

**판단:** A 대비 B·C에서 `phase_lag_ms`와 `game_state_ms`가 함께 커지면, 채팅이 게임 잠금을 오래 잡아 게임 진행을 밀고 있다는 뜻이다.
`chat_send_ms`만 커지고 phase_lag가 그대로면, 경합보다는 채팅 저장 경로(Redis 왕복) 자체가 원인이다.

## 3. 서버 쪽에서 같이 볼 것

- 서버 로그에서 `LockTimeoutException` / `LOCK_BUSY` 검색
- (Redisson) 잠금 키 대기: 부하 중 `docker exec -it whoisntcitizen-redis redis-cli --latency`, `INFO commandstats`의 `evalsha`(Redisson 잠금은 Lua)
- 스레드 덤프나 JFR(`-XX:StartFlightRecording`)의 Java Monitor Blocked / Thread Park 이벤트로 잠금 대기 위치 확인 (단일 서버 모드)

## 개선 아이디어 (측정 뒤 비교용)

- 채팅 전송에서 게임 잠금을 잡지 않고 페이즈·생존·해적 여부를 스냅숏(읽기 전용 복사본)으로 판단
- 채팅 조회 `Visibility.resolve()`의 잠금 제거 (요청 1회당 한 번만 읽으므로 잠금 없이 최신 상태를 읽어도 된다)
- 개선한 뒤 같은 A~D를 다시 돌려 위 표에 한 줄씩 추가한다
