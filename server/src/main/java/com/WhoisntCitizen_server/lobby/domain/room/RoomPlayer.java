package com.WhoisntCitizen_server.lobby.domain.room;

import lombok.AllArgsConstructor;
import lombok.Getter;
import lombok.NoArgsConstructor;

@Getter
@AllArgsConstructor
@NoArgsConstructor
public class RoomPlayer {

    private Long userId;
    private String nickname;
    private boolean ready;
    // 입장할 때의 프로필 이미지 번호. 대기실 초상화에 쓴다. 이 필드가 없던 방 데이터는 기본 아바타(0)로 읽힌다.
    private int profileImageId;

    public RoomPlayer(Long userId, String nickname, boolean ready) {
        this(userId, nickname, ready, 0);
    }

    /** 준비 / 준비 취소. (인자가 있는 메서드라 Jackson이 JSON 속성으로 보지 않는다) */
    public void changeReady(boolean ready) {
        this.ready = ready;
    }

    /** 게임이 끝나 방으로 돌아올 때 준비 상태를 해제한다. */
    public void resetReady() {
        this.ready = false;
    }
}
