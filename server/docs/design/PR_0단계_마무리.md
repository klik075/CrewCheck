## 개요

멀티 서버(Redis) 전환을 위한 **0단계: 인터페이스 분리**의 마무리 PR입니다.
방 잠금을 인터페이스로 분리하고(0-9), 주석·옛 코드·문서를 정리했습니다(0-10).

> **동작 변화 없음.** 구현체는 모두 기존과 같은 서버 메모리 방식(`Local*`)이고, 잠금 범위·순서·게임 규칙은 바뀌지 않았습니다.

## 이번 PR 변경 사항

### 0-9. 방 잠금 분리 (`RoomLockManager` → `RoomLock`)
- `lobby/lock/RoomLock` 인터페이스 추가: `withLock(roomId, Supplier)`, `runWithLock(roomId, Runnable)`
- `lobby/lock/LocalRoomLock` 추가: roomId별 `ReentrantLock`. 예전 `RoomLockManager`(roomId별 `synchronized`)와 같은 동작 (재진입 가능, 다른 방끼리는 대기 없음)
- `lobby/config/RoomLockConfig`에서 Bean 등록
- `RoomService`: `RoomLockManager roomLockManager` → `RoomLock roomLock` (잠금 사용 7곳, 로직 변경 없음)
- `RoomLockManager` 삭제
- 테스트: `LocalRoomLockTest` 추가, `RoomServiceEndlessGameTest`는 `new LocalRoomLock()` 사용

### 0-10. 정리
- `Game`: 주석 처리된 옛 코드 삭제 (옛 `nightActions` 필드, 옛 `recordNightAction`·`allNightActionsSubmitted`, 옛 `getNightActions`)
- `GameConfig`: 주석 처리된 `scheduler.initialize()`를 "Spring이 `afterPropertiesSet()`으로 초기화하므로 직접 부르지 않는다"는 설명으로 교체
- `Claude outputs/` 문서 갱신
  - `08_공통_타이머_동시성_예외.md`: 잠금·타이머·접속 기록을 현재 코드로 갱신하고, "7) 0단계 인터페이스" 절과 저장 규칙 추가
  - 01·02·03·04·05·07 문서와 README에 남아 있던 `synchronized(game)`, `RoomLockManager`, `game.touch` 설명을 현재 코드로 수정

## 0단계 전체: 분리한 인터페이스 목록

| 인터페이스 | 지금 구현 (Bean) | 하는 일 | Redis로 바꿀 단계 |
| --- | --- | --- | --- |
| `GameRepository` | `InMemoryGameRepository` | 게임 상태 저장·조회 | 1단계: Redis 저장 |
| `GameLock` | `LocalGameLock` | 게임 단위 잠금 | 2단계: 분산 잠금 |
| `RoomLock` | `LocalRoomLock` | 방 단위 잠금 | 2단계: 분산 잠금 |
| `GameTimer` | `LocalGameTimer` | 페이즈 제한 시간, 종료 게임 정리 예약 | 3단계: Redis ZSET 타이머 |
| `GameTimeoutHandler` | `GameFlowService` | 타이머가 울리면 호출됨 (`onPhaseTimeout`, `onCleanup`) | 그대로 |
| `DeferredEventPublisher` | `SchedulerDeferredEventPublisher` | 게임 잠금이 풀린 뒤 로비 이벤트 발행 | 5단계: Redis Pub/Sub |
| `PlayerActivityTracker` | `LocalPlayerActivityTracker` | 플레이어 마지막 요청 시각 | 6단계: Redis TTL |

다음 단계부터는 위 Bean만 Redis 구현으로 바꾸고, 서비스 코드(`GameFlowService`, `NightService`, `VoteService`, `RoomService` 등)는 그대로 둡니다.

### 0단계 커밋 (앞 PR #33, #36 포함)

| 작업 | 커밋 |
| --- | --- |
| 0-1 스케줄러가 주입한 `Clock` 사용 | `1f8afef` |
| 0-2~0-4 `GameTimer`, `DeferredEventPublisher` 분리 | `a4c4746` |
| 0-5~0-7 `PlayerActivityTracker`, `GameLock` 분리 (`synchronized` 제거) | `ca8e53b` |
| 0-8 저장 누락 점검 테스트 (`CopyingGameRepository`, `GameSaveDisciplineTest`) | `4b7d002` |
| 머지 후 컴파일 오류 수정 | `9268f8f` |
| 0-9 `RoomLock` 분리 | `64f97ce` |
| 0-10 정리 | 이번 PR |

## 사용 규칙 (팀 공유)

- **잠금을 먼저 잡고, 잠금 안에서 저장소에서 다시 읽고, 바꿨으면 `save()` 후 잠금을 푼다.** 게임·방 객체를 잠금 밖으로 가지고 나가지 않는다. (Redis에서는 잠금 밖에서 읽은 객체가 오래된 복사본이다)
- 잠금 순서는 **방 잠금 → 게임 잠금**만 허용한다. 게임 잠금 안에서 로비(방 잠금)를 부르는 이벤트는 `DeferredEventPublisher`로 잠금 밖에서 발행한다.
- 새로 방 잠금이 필요하면 `RoomLock`을, 게임 잠금이 필요하면 `GameLock`을 주입받아 쓴다. `synchronized`를 섞어 쓰지 않는다 (`synchronized`와 `ReentrantLock`은 서로를 막지 못한다).
- 게임 상태를 바꾸는 코드를 추가하면 `GameSaveDisciplineTest`에 경로를 추가한다.

## 1단계 전에 남은 일

- [ ] `ChatMessageService.activeGame`이 잠금 밖에서 `findById`를 함 → 잠금 안에서 읽도록 수정 (채팅 담당과 협의)

## 테스트

- [ ] `./gradlew test` 통과
- 새 테스트: `LocalRoomLockTest` (결과 반환, 재진입, 같은 방 대기, 다른 방 비대기, 예외 시 잠금 해제, null roomId 거부)
