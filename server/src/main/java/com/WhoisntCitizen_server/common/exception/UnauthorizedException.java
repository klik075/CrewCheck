package com.WhoisntCitizen_server.common.exception;

/** 인증 실패(리프레시 토큰이 없거나 만료·폐기·불일치): 401. Unity는 이때 세션을 정리하고 타이틀로 돌아간다. */
public class UnauthorizedException extends RuntimeException {

	public UnauthorizedException(String message) {
		super(message);
	}
}
