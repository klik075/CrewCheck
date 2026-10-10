package com.WhoisntCitizen_server.common.exception;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.data.redis.RedisConnectionFailureException;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.http.converter.HttpMessageNotReadableException;
import org.springframework.web.bind.MethodArgumentNotValidException;
import org.springframework.web.bind.MissingRequestHeaderException;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.RestControllerAdvice;
import org.springframework.web.method.annotation.MethodArgumentTypeMismatchException;

@RestControllerAdvice
public class GlobalExceptionHandler {

    private static final Logger log = LoggerFactory.getLogger(GlobalExceptionHandler.class);

    @ExceptionHandler(GameNotFoundException.class)
    public ResponseEntity<ErrorResponse> handleNotFound(GameNotFoundException e) {
        return ResponseEntity.status(HttpStatus.NOT_FOUND).body(new ErrorResponse("GAME_NOT_FOUND", e.getMessage()));
    }

    /** 페이즈가 맞지 않거나 규칙 위반: 409 Conflict */
    @ExceptionHandler(GameRuleException.class)
    public ResponseEntity<ErrorResponse> handleRule(GameRuleException e) {
        return ResponseEntity.status(HttpStatus.CONFLICT).body(new ErrorResponse("GAME_RULE_VIOLATION", e.getMessage()));
    }

    @ExceptionHandler({MethodArgumentNotValidException.class, MissingRequestHeaderException.class})
    public ResponseEntity<ErrorResponse> handleBadRequest(Exception e) {
        return ResponseEntity.badRequest().body(new ErrorResponse("BAD_REQUEST", e.getMessage()));
    }

    /** 존재하지 않는 방/유저, 잘못된 입력 등: 400 */
    @ExceptionHandler(IllegalArgumentException.class)
    public ResponseEntity<ErrorResponse> handleIllegalArgument(IllegalArgumentException e) {
        return ResponseEntity.badRequest().body(new ErrorResponse("BAD_REQUEST", e.getMessage()));
    }

    /** 방이 가득 참, 이미 참가 중 등 현재 상태와 충돌: 409 */
    @ExceptionHandler(IllegalStateException.class)
    public ResponseEntity<ErrorResponse> handleIllegalState(IllegalStateException e) {
        return ResponseEntity.status(HttpStatus.CONFLICT).body(new ErrorResponse("CONFLICT", e.getMessage()));
    }

    // ---------- 로비 ----------

    /** 비밀방 비밀번호 없음/불일치: 403. Unity는 code로 비밀번호 재입력 여부를 판단한다 */
    @ExceptionHandler(RoomPasswordException.class)
    public ResponseEntity<ErrorResponse> handleRoomPassword(RoomPasswordException e) {
        return ResponseEntity.status(HttpStatus.FORBIDDEN).body(new ErrorResponse(RoomPasswordException.CODE, e.getMessage()));
    }

    /** 추방된 방에 다시 입장하려 함: 403. 비밀번호 틀림과 구분하도록 code를 따로 쓴다 */
    @ExceptionHandler(RoomKickedException.class)
    public ResponseEntity<ErrorResponse> handleRoomKicked(RoomKickedException e) {
        return ResponseEntity.status(HttpStatus.FORBIDDEN).body(new ErrorResponse(RoomKickedException.CODE, e.getMessage()));
    }

    // ---------- 채팅 ----------

    /** 채팅 대상 방 없음: 404 */
    @ExceptionHandler(NotFoundException.class)
    public ResponseEntity<ErrorResponse> handleNotFound(NotFoundException e) {
        return ResponseEntity.status(HttpStatus.NOT_FOUND).body(new ErrorResponse("NOT_FOUND", e.getMessage()));
    }

    /** 권한 없음: 403 (채팅할 수 없는 플레이어, 공지 권한 없음, 방장 전용 기능을 방장이 아닌 사람이 요청) */
    @ExceptionHandler(ForbiddenException.class)
    public ResponseEntity<ErrorResponse> handleForbidden(ForbiddenException e) {
        return ResponseEntity.status(HttpStatus.FORBIDDEN).body(new ErrorResponse("FORBIDDEN", e.getMessage()));
    }

    /** JSON 본문을 읽을 수 없음: 400 */
    @ExceptionHandler(HttpMessageNotReadableException.class)
    public ResponseEntity<ErrorResponse> handleUnreadable(HttpMessageNotReadableException e) {
        return ResponseEntity.badRequest().body(new ErrorResponse("BAD_REQUEST", "요청 본문(JSON)을 읽을 수 없습니다."));
    }

    /** 경로 변수/파라미터 타입 불일치 (예: roomId에 문자): 400 */
    @ExceptionHandler(MethodArgumentTypeMismatchException.class)
    public ResponseEntity<ErrorResponse> handleTypeMismatch(MethodArgumentTypeMismatchException e) {
        return ResponseEntity.badRequest().body(new ErrorResponse("BAD_REQUEST", e.getMessage()));
    }

    /**
     * 게임·방 잠금을 대기 시간 안에 잡지 못함: 503 + Retry-After: 1
     * 같은 게임(방)에 요청이 몰렸거나 다른 서버가 오래 쥐고 있는 경우다. 요청한 작업은 실행되지 않았으므로
     * 클라이언트는 잠시 뒤 같은 요청을 다시 보내면 된다. (code: LOCK_BUSY)
     */
    @ExceptionHandler(LockTimeoutException.class)
    public ResponseEntity<ErrorResponse> handleLockTimeout(LockTimeoutException e) {
        log.warn("잠금 대기 시간 초과: {}", e.getMessage());
        return ResponseEntity.status(HttpStatus.SERVICE_UNAVAILABLE)
                .header(HttpHeaders.RETRY_AFTER, "1")
                .body(new ErrorResponse("LOCK_BUSY", "요청이 몰려 처리하지 못했습니다. 잠시 후 다시 시도해 주세요."));
    }

    /** Redis 서버에 연결할 수 없음 (로비 방, 채팅 메시지): 503 */
    @ExceptionHandler(RedisConnectionFailureException.class)
    public ResponseEntity<ErrorResponse> handleRedisDown(RedisConnectionFailureException e) {
        log.error("Redis 연결 실패: {}", e.getMessage());
        return ResponseEntity.status(HttpStatus.SERVICE_UNAVAILABLE)
                .body(new ErrorResponse("REDIS_UNAVAILABLE", "Redis 서버에 연결할 수 없습니다."));
    }
}
