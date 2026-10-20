# WebSocket 메시지 규칙 (v1)

서버 1대 기준 4단계에서 쓰는 실시간 알림 규칙입니다. 코드: `realtime/protocol` 패키지.

## 1. 역할

| 통로 | 방향 | 하는 일 |
|---|---|---|
| WebSocket `/ws/v1` | 서버 → 클라이언트 | "바뀌었어" 신호만 보냄 (내용 없음) |
| REST (지금 API 그대로) | 클라이언트 → 서버 | 신호를 받은 뒤 내용 조회(GET), 투표·밤 행동·채팅 보내기(POST) |

- 보이는 정보 처리(역할 비공개, 해적 전용, 사망자 채팅)는 REST 응답에만 있습니다. WebSocket에는 비밀 정보가 실리지 않습니다.
- 신호를 놓쳐도 다음 GET이 최신 상태를 가져옵니다. 연결이 끊겨 있으면 예전처럼 polling합니다.

## 2. 연결과 인증

```
ws://{서버}:8080/ws/v1                       + 헤더 Authorization: Bearer {JWT}   (데스크톱·모바일)
ws://{서버}:8080/ws/v1?token={JWT}                                                (WebGL 등 헤더를 못 넣을 때)
```

- JWT는 로그인 때 받은 REST용 토큰과 같습니다.
- 둘 다 있으면 헤더를 씁니다. Bearer가 아닌 Authorization 헤더는 토큰이 없는 것으로 봅니다.
- 토큰이 없거나 틀리거나 만료됐으면, 연결은 열리지만 서버가 아무 메시지도 보내지 않고 **바로 close code `4401`로 닫습니다.**
  - HTTP 401로 거절하지 않는 이유: Unity(데스크톱 `ClientWebSocket`)와 브라우저(WebGL) 모두 HTTP 401을 "연결 실패"로만 알려 줘서 토큰 문제인지 알 수 없습니다. `4401`은 두 플랫폼 모두 그대로 전달됩니다.
  - 클라이언트는 `4401`을 받으면 재연결을 멈추고 다시 로그인합니다. (연결 중 토큰 만료와 같은 처리)
- 연결되면 서버가 먼저 `connected`를 보냅니다.

## 3. 메시지 모양

모든 메시지는 `type`을 가진 평평한 JSON 객체입니다. 모르는 필드는 무시합니다.

### 서버 → 클라이언트

| type | 필드 | 받으면 할 일 |
|---|---|---|
| `connected` | `userId`, `heartbeatSeconds`(10), `idleTimeoutSeconds`(30) | 보고 있는 방을 `room.subscribe`로 알림. ping 시작 |
| `pong` | `serverTime`(epoch ms) | 없음 (서버 시계 차이 계산에 써도 됨) |
| `subscribed` | `roomId` | 구독 완료. 이때 방·게임·채팅을 GET으로 한 번 가져옴 (구독 전 변경 보충) |
| `game.changed` | `gameId`, `phaseVersion` | `GET /api/v1/games/{gameId}` (phaseVersion은 참고용, 항상 다시 조회) |
| `room.changed` | `roomId` | `GET /api/v1/rooms/{roomId}` |
| `room.created` | `roomId` | (로비) 새로고침 버튼의 RedDot만 켬. 목록은 자동으로 바꾸지 않음. **방을 구독하지 않은 연결(로비)에만** 가고 만든 사람은 받지 않음. 인원·상태 변경·삭제는 이 신호를 보내지 않음 |
| `chat.changed` | `roomId`, `lastMessageId` | 이미 받은 id보다 크면 `GET /api/v1/rooms/{roomId}/messages?afterId=...` (사망자·해적·세이렌 전용 메시지는 볼 수 있는 사람에게만 옴) |
| `error` | `code`, `message` | 로그. 연결은 유지됨 |

```json
{"type":"connected","userId":15,"heartbeatSeconds":10,"idleTimeoutSeconds":30}
{"type":"subscribed","roomId":3}
{"type":"game.changed","gameId":"abc","phaseVersion":7}
{"type":"room.changed","roomId":3}
{"type":"room.created","roomId":7}
{"type":"chat.changed","roomId":3,"lastMessageId":42}
{"type":"error","code":"NOT_IN_ROOM","message":"..."}
```

### 클라이언트 → 서버

| type | 필드 | 언제 |
|---|---|---|
| `ping` | 없음 | 10초마다. 서버는 `pong`으로 답하고 접속 기록을 남김 |
| `room.subscribe` | `roomId` | 방 화면·게임 화면에 들어갈 때. 그 방 참가자면 `subscribed`, 아니면 `error(NOT_IN_ROOM)`. 연결 하나는 방 하나만 구독(새로 구독하면 이전 구독은 풀림) |
| `room.unsubscribe` | `roomId` | 방에서 나갈 때 (답 없음) |

퇴장·강퇴·게임 중 연결 끊김으로 방에서 빠지거나 방이 삭제되면 서버가 구독을 자동으로 풉니다. 그 직전에 `room.changed`를 한 번 받으니, GET 결과로 방에서 빠졌는지 확인하세요.

```json
{"type":"ping"}
{"type":"room.subscribe","roomId":3}
```

### error code

| code | 뜻 |
|---|---|
| `BAD_MESSAGE` | JSON이 아님, type 없음, roomId 없음, 4096자 초과 |
| `UNKNOWN_TYPE` | 모르는 type |
| `NOT_IN_ROOM` | 들어 있지 않은 방을 구독하려 함 |
| `SERVER_ERROR` | 서버 쪽 문제로 처리 못 함. 잠시 뒤 다시 보내면 됨 |

## 4. 연결 유지와 끊김

- 클라이언트는 10초마다 `ping`을 보냅니다.
- 서버는 30초 동안 아무 메시지가 없으면 연결을 닫습니다 (close code `4408`).
- 연결 중 토큰이 만료되면 서버가 닫습니다 (close code `4401`). 다시 로그인한 뒤 연결합니다. 연결할 때 토큰이 잘못됐을 때도 같은 코드입니다.
- 끊기면 클라이언트는 1초, 2초, 4초 ... 최대 30초 간격으로 다시 연결하고, 그동안은 예전 polling으로 화면을 갱신합니다.
- 다시 연결되면 `room.subscribe`를 다시 보내고, 보고 있던 화면을 GET으로 한 번 새로 고칩니다(끊긴 동안 놓친 신호 보충).
- 연결이 끊겨도 바로 사망 처리되지 않습니다. 연결 끊김 판정은 기존처럼 마지막 요청 후 60초입니다.

## 5. 유저 접속 상태 (친구 목록의 접속 표시)

별도 메시지는 없습니다. 연결과 `ping`, `room.subscribe`/`room.unsubscribe`만으로 서버가 Redis(`user:presence:{userId}`, TTL 60초)에 기록하고, `GET /api/friends`가 읽어서 `online`·`status`로 내려줍니다. (예전 `POST /api/users/ping`은 없어졌습니다)

| status | 언제 |
|---|---|
| `ONLINE` | 연결돼 있고 방을 구독하지 않음 (로비 등) |
| `IN_ROOM` | 진행 중인 게임이 없는 방을 구독 중 |
| `IN_GAME` | 진행 중인 게임이 있는 방을 구독 중 |
| `OFFLINE` | 연결 없음 (Redis 키 없음) |

- 갱신 시점: 연결, 10초마다의 `ping`, 구독·해제. `ping`마다 "지금 구독 중인 방"으로 다시 계산하므로, 방 삭제·강퇴·게임 시작은 최대 10초 안에 반영됩니다.
- 끊기면: 그 유저의 연결이 하나도 안 남았을 때만 바로 `OFFLINE`으로 바꿉니다. (두 기기, 재연결이 겹치는 순간에는 유지)
- 서버가 죽어 끊김을 못 받아도 TTL(60초)이 지나면 `OFFLINE`이 됩니다.
- 기록이 실패해도(Redis 장애 등) 연결·`pong`에는 영향이 없습니다. 친구 목록은 조회가 실패하면 모두 `OFFLINE`으로 보여 줍니다.
- 서버를 여러 대로 늘리면, 다른 서버에 두 번째 연결이 남은 채 한쪽이 끊길 때 키가 잠깐 지워질 수 있습니다. 남은 연결의 다음 `ping`(10초 이내)에서 되살아납니다.
