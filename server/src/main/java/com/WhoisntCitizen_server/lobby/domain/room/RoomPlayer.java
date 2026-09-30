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

    /** 게임이 끝나 방으로 돌아올 때 준비 상태를 해제한다. */
    public void resetReady() {
        this.ready = false;
    }
}
