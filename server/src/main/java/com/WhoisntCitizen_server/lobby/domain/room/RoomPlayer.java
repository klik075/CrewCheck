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
}