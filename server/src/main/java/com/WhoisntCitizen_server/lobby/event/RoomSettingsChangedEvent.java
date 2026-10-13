package com.WhoisntCitizen_server.lobby.event;

import java.util.Objects;

/**
 * 방장이 방 설정(제목, 최대 인원, 공개/비밀, 비밀번호)을 바꿨을 때 발행되는 이벤트.
 * 채팅(ChatLobbyEventListener)이 받아 "방 설정이 변경되었습니다: ..." 시스템 메시지를 남긴다.
 * 실제로 바뀐 것이 있을 때만 발행된다.
 *
 * ⚠ 비밀번호 값은 절대 담지 않는다. 바뀌었는지 여부(passwordChanged)만 알린다.
 *
 * @param before          변경 전 설정
 * @param after           변경 후 설정
 * @param passwordChanged 비밀방이 비밀방으로 유지되면서 비밀번호가 다른 값으로 바뀌었으면 true.
 *                        (공개방 → 비밀방 전환은 privateRoom 변화로 알리므로 여기서는 false)
 */
public record RoomSettingsChangedEvent(Long roomId, Settings before, Settings after, boolean passwordChanged) {

    /** 채팅에 알려도 되는 방 설정 값만 담는다. (비밀번호 없음) */
    public record Settings(String title, int maxPlayers, boolean privateRoom) {
    }

    public boolean titleChanged() {
        return !Objects.equals(before.title(), after.title());
    }

    public boolean maxPlayersChanged() {
        return before.maxPlayers() != after.maxPlayers();
    }

    public boolean privateRoomChanged() {
        return before.privateRoom() != after.privateRoom();
    }
}
