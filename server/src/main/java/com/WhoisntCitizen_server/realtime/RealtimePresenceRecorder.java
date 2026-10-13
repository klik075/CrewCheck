package com.WhoisntCitizen_server.realtime;

import java.util.OptionalLong;

/**
 * WebSocket 연결 상태를 유저 접속 상태(온라인/대기실/게임 중/오프라인)에 반영한다.
 * 친구 목록의 "접속 중" 표시가 이 기록을 읽는다.
 *
 * memberId는 연결 때 확인한 토큰의 sub(계정 id)다. userId(프로필 id)로 바꾸는 일은 구현이 한다.
 */
public interface RealtimePresenceRecorder {

    /**
     * 연결됐을 때, ping을 받을 때, 방을 구독·해제했을 때 부른다.
     * roomId = 이 연결이 지금 구독 중인 방(없으면 empty). 방에 진행 중인 게임이 있으면 IN_GAME, 없으면 IN_ROOM, 방이 없으면 ONLINE으로 기록한다.
     */
    void update(long memberId, OptionalLong roomId);

    /** 이 유저의 연결이 모두 끊겼을 때 부른다. 오프라인으로 바꾼다 */
    void offline(long memberId);
}
