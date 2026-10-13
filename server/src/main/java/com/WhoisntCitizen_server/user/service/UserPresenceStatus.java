package com.WhoisntCitizen_server.user.service;

/**
 * 유저의 접속 상태. Redis(user:presence:{userId})에는 OFFLINE을 제외한 값만 저장한다. 키가 없으면 OFFLINE이다.
 *
 *  - ONLINE  : 접속 중 (로비 등, 방에 들어가 있지 않음)
 *  - IN_ROOM : 대기실(방)에 있음
 *  - IN_GAME : 진행 중인 게임이 있는 방에 있음
 *  - OFFLINE : 접속하지 않음
 */
public enum UserPresenceStatus {
	OFFLINE,
	ONLINE,
	IN_ROOM,
	IN_GAME;

	public boolean isOnline() {
		return this != OFFLINE;
	}

	/** Redis에 저장된 값을 읽는다. 비어 있거나 모르는 값이면 OFFLINE. (예전 하트비트가 남긴 "ONLINE"도 그대로 읽힌다) */
	public static UserPresenceStatus parse(String value) {
		if (value == null) return OFFLINE;
		try {
			return valueOf(value);
		} catch (IllegalArgumentException e) {
			return OFFLINE;
		}
	}
}
