package com.WhoisntCitizen_server.common.exception;

import org.junit.jupiter.api.Test;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;

import java.time.Duration;

import static org.assertj.core.api.Assertions.assertThat;

/** 잠금 대기 시간 초과가 API 응답으로 어떻게 바뀌는지 확인한다. */
class GlobalExceptionHandlerLockTimeoutTest {

    private final GlobalExceptionHandler handler = new GlobalExceptionHandler();

    @Test
    void 잠금_대기_시간_초과는_503과_LOCK_BUSY로_응답하고_1초_뒤_다시_시도하라고_알린다() {
        ResponseEntity<ErrorResponse> response =
                handler.handleLockTimeout(new LockTimeoutException("게임", "game-1", Duration.ofSeconds(10)));

        assertThat(response.getStatusCode()).isEqualTo(HttpStatus.SERVICE_UNAVAILABLE);
        assertThat(response.getHeaders().getFirst(HttpHeaders.RETRY_AFTER)).isEqualTo("1");
        assertThat(response.getBody()).isNotNull();
        assertThat(response.getBody().code()).isEqualTo("LOCK_BUSY");
    }

    @Test
    void 예외에는_어떤_잠금인지와_대상과_기다린_시간이_담긴다() {
        LockTimeoutException e = new LockTimeoutException("방", 7L, Duration.ofMillis(300));

        assertThat(e.getLockName()).isEqualTo("방");
        assertThat(e.getKey()).isEqualTo(7L);
        assertThat(e.getWaited()).isEqualTo(Duration.ofMillis(300));
        assertThat(e.getMessage()).contains("방", "300ms", "key=7");
    }
}
