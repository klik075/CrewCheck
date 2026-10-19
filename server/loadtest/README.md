# 로비/대기실 부하테스트 계획

2026-10-18 · 유진

대상 기능(내가 작성한 코드): 대기실 접속 기록과 연결 끊김 처리(`lobby/presence`), 방 목록·검색(`RoomService.getRooms`)

## 1. 왜 이 주제인가

| 주제 | 배경 | 이번에 밝히고 싶은 것 |
| --- | --- | --- |
| **A. 대기실 억울한 퇴장** (메인) | `2f27052`에서 실제로 발생했다. WebSocket 구독 중에는 방 조회가 15초 간격이라 조회가 한 번만 늦어도 30초 기준에 걸렸다. 기준을 45초로 늘려 **임시 대응**만 한 상태다. | 45초가 부하와 네트워크 흔들림 속에서도 안전한가? 검사 루프(`checkAll`) 자체가 방 수에 비례해 느려지지 않는가? |
| **B. 방 목록 N+1** (서브) | `getRooms()` = `SMEMBERS` 1번 + 방마다 `GET` 1번. 방 N개면 Redis 왕복이 N+1번이고 직렬로 돈다. | 방 수가 늘 때 목록 조회 p95가 얼마나 나빠지나? `MGET`으로 바꾸면 얼마나 좋아지나? |

## 2. 가설

- **H1. 조회 누락만으로 퇴장된다.** 조회 주기가 15초라서 연속 2회 누락이면 공백이 45초가 되어 기준선에 닿는다. 부하가 없어도 `DROP_RATE`가 높으면 억울한 퇴장이 생긴다.
- **H2. 부하가 공백을 늘린다.** 서버가 밀리면 `GET /rooms/{id}`의 응답이 늦어지고, 다음 조회도 그만큼 밀린다. k6 VU는 응답을 기다린 뒤 다음 주기를 돈다. Unity도 코루틴에서 응답을 기다리면 같다. 결과적으로 같은 누락률에서도 퇴장이 늘어난다.
- **H3. 검사 루프가 방 수에 비례해 느려지고 다른 작업까지 늦춘다.**
  - `checkAll()`은 스레드 1개로 모든 방을 순서대로 검사한다. 방마다 Redis 왕복이 3~4번이고, 빼야 할 사람이 있으면 방 잠금을 잡는다(최대 10초 대기).
  - 이 작업은 **`gamePhaseScheduler`(풀 4개)를 게임 페이즈 타이머, InactivePlayerMonitor, WebSocket 연결 감시, 잠금 재시도와 같이 쓴다.** 그래서 검사가 길어지면 게임 쪽 타이머까지 밀릴 수 있다.
- **H4. 방 목록 조회는 방 수에 선형으로 느려진다.** `MGET`이나 파이프라인으로 바꾸면 방 1000개에서 p95가 크게 준다.

## 3. 준비

### 3-1. 도구

```bash
brew install k6
docker compose up -d            # 로컬 MySQL + Redis (프로젝트 루트)
```

### 3-2. 서버에 지표 노출 (측정용, 브랜치에서만)

`build.gradle`
```gradle
implementation 'io.micrometer:micrometer-registry-prometheus'
```
`application.properties` (부하테스트 프로필에서만)
```properties
management.endpoints.web.exposure.include=health,prometheus,metrics
management.metrics.distribution.percentiles-histogram.http.server.requests=true
server.tomcat.mbeanregistry.enabled=true
```

### 3-3. 대기실 검사에 커스텀 지표 추가 (H3 증명용)

`RoomPresenceService`에 `MeterRegistry`를 주입하고 다음처럼 바꾼다.

```java
// checkAll() 전체 소요 시간: 이게 idle-check-seconds(5초)를 넘으면 검사가 밀리고 있다는 뜻
public void checkAll() {
    Timer.Sample sample = Timer.start(meterRegistry);
    try { /* 기존 내용 */ }
    finally { sample.stop(meterRegistry.timer("lobby.presence.check.duration")); }
}
// 검사한 방 수, 퇴장 처리한 사람 수
meterRegistry.gauge("lobby.presence.rooms", ids.size());
meterRegistry.counter("lobby.presence.removed").increment(removed.size());
// 잠금이 바빠 건너뛴 방 수
meterRegistry.counter("lobby.presence.lock_skipped").increment();
```

게임 타이머 지연도 같이 보려면 `RedisTimerDispatcher`에서 `실행 시각 - 예약 시각`을 `game.timer.lag` Timer로 기록한다.

### 3-4. 관측 대시보드

```bash
docker compose -f loadtest/monitoring/docker-compose.yml up -d
# Grafana http://localhost:3000 (admin/admin) → Prometheus 데이터소스 http://prometheus:9090
```

## 4. 시나리오와 실행 순서

모든 명령은 `loadtest/k6`에서 실행한다. 결과 JSON은 `loadtest/results/`에 쌓인다(git 제외).

### 시나리오 A — 대기실 억울한 퇴장 (`s1_lobby_presence.js`)

ROOMS개 방 × PLAYERS명이 대기실에 머문다. 각 참가자의 동작은 실제 Unity와 같다.

- WebSocket에 연결해 `room.subscribe`를 보내고, 10초마다 `ping`을 보낸다.
- `GET /rooms/{id}`는 15초마다 보낸다.
- 응답 명단에서 내가 빠지면 **억울한 퇴장**으로 센다.

| 단계 | 명령 | 확인할 것 |
| --- | --- | --- |
| A0 기준선 | `k6 run -e ROOMS=50 -e PLAYERS=4 -e HOLD=5m s1_lobby_presence.js` | 억울한 퇴장 0, 조회 p95 |
| A1 원래 장애 재현 | 서버를 `MAFIA_LOBBY_IDLE_TIMEOUT_SECONDS=30`으로 띄우고 `-e DROP_RATE=0.1` | 30초 기준에서 퇴장이 나는지 (H1) |
| A2 현재 45초 + 누락 | 기본 서버 + `DROP_RATE` 0.05 / 0.1 / 0.2 | 누락률별 퇴장 수. 0이 아니면 45초도 불충분 |
| A3 부하 + 누락 | `-e ROOMS=300 -e BROWSERS=200 -e DROP_RATE=0.05` | 조회 p95 상승 → 퇴장 증가 (H2), `lobby.presence.check.duration` (H3) |
| A4 방 수 늘리기 | ROOMS 100 → 500 → 1000 (`PLAYERS=4`, 로컬 VU 한도에 맞춰 조절) | 검사 1바퀴 시간 vs 방 수 그래프, `game.timer.lag` |
| A5 비교 | `-e WS=0` (예전 1초 폴링) | 폴링 방식이면 퇴장이 없지만 요청 수가 15배 |

### 시나리오 B — 방 목록 N+1 (`s2_room_list.js`)

서버를 `MAFIA_LOBBY_IDLE_TIMEOUT_SECONDS=0`(연결 끊김 처리 OFF)으로 띄운다. 방장이 조회하지 않아도 방이 지워지지 않게 하기 위해서다.

```bash
../redis-commandstats.sh before
k6 run -e ROOMS=100  -e VUS=100 s2_room_list.js
../redis-commandstats.sh after     # cmdstat_get 증가량 / 목록 요청 수 = 요청당 GET 횟수
# ROOMS=500, 1000으로 반복 (방은 누적되므로 Redis를 비우거나 100 → 500 → 1000 순서로)
k6 run -e ROOMS=1000 -e KEYWORD=초보 s2_room_list.js   # 검색도 전체를 읽는지 확인
```

## 5. 볼 지표

| 지표 | 어디서 | 의미 |
| --- | --- | --- |
| `lobby_false_kicks`, `lobby_stayed` | k6 | **핵심 결과**. 억울한 퇴장 수와 끝까지 남은 비율 |
| `lobby_poll_duration` p95/p99 | k6 | 대기실 조회 지연. 접속 기록이 늦게 찍히는 정도 |
| `lobby_poll_gap_max_s` | k6 | 참가자가 겪은 최대 조회 공백. 45초에 얼마나 가까운가 |
| `room_list_duration` p95, `http_reqs` rate | k6 | 방 목록 응답 시간과 처리량 |
| `lobby.presence.check.duration` | Prometheus (3-3) | 검사 1바퀴 시간. 5초를 넘으면 위험 |
| `game.timer.lag` | Prometheus (3-3) | 대기실 검사가 게임 타이머를 늦추는지 (H3) |
| `tomcat.threads.busy`, `http.server.requests` | Prometheus | 서버 포화 여부 |
| `cmdstat_get` calls | `redis-commandstats.sh` | 목록 요청 1번당 Redis GET 횟수 (N+1 증명) |
| 스레드 덤프 `game-phase-*` | `jcmd <pid> Thread.print` | 검사가 잠금 대기로 스레드를 붙잡는지 |

## 6. 개선 후보 (측정 후 하나씩 적용 → 같은 시나리오 재측정)

**A. 대기실 퇴장**
1. **WebSocket `ping`도 대기실 접속 기록으로 남긴다.** 커밋 메시지에 적어 둔 근본 해결이다. 기록 주기가 15초에서 10초로 줄고, HTTP 지연과도 분리된다. 적용 후 타임아웃을 다시 줄일 수 있는지 측정한다.
2. 검사 루프 개선: 방을 Redis 파이프라인으로 한꺼번에 읽기, 잠금 대기 시간을 짧게(검사용 `tryLock(0)`), 오래 걸리면 전용 스케줄러로 분리해 게임 타이머와 격리.
3. (선택) 접속 기록을 방 단위 Hash 대신 ZSET(score = 마지막 시각)으로 바꿔 `ZRANGEBYSCORE`로 오래된 사람만 뽑기.

**B. 방 목록**
1. `findById` 반복을 `multiGet`(MGET) 한 번으로 바꾼다.
2. 목록 응답을 짧게(1~2초) 캐시한다. `room.created`/`room.changed` 신호가 있으니 무효화도 가능하다.

## 7. 결과 기록 양식

| 실행 | 조건 | 억울한 퇴장 | 조회 p95 | 검사 1바퀴 | 비고 |
| --- | --- | --- | --- | --- | --- |
| A0 | 50방×4, DROP 0 | | | | |
| A2 | 50방×4, DROP 0.1 | | | | |
| A3 | 300방×4 + 200 브라우저, DROP 0.05 | | | | |
| A3' | (개선 1 적용 후) 같은 조건 | | | | |

| 실행 | 방 수 | 목록 p95 | req/s | 요청당 GET | 비고 |
| --- | --- | --- | --- | --- | --- |
| B 개선 전 | 100 / 500 / 1000 | | | | |
| B 개선 후 (MGET) | 100 / 500 / 1000 | | | | |

트러블슈팅 문서는 **증상 → 가설 → 근거(그래프·덤프·수치) → 해결 → 전후 비교** 순서로 정리한다.

## 주의

- 게스트 계정이 실행마다 새로 생긴다(`lt-<runId>-...`). 로컬 DB에서만 돌리고, 필요하면 username이 `guest_lt-`로 시작하는 게스트 계정을 정리한다.
- 로컬 Mac에서 k6 VU 1000개 이상, WebSocket 1000개 이상은 k6 자체가 병목일 수 있다. `ulimit -n 65535`를 먼저 해 두고 k6 CPU 사용률을 같이 본다.
- 운영 서버에는 돌리지 않는다.
