package com.WhoisntCitizen_server.lobby.dto;

import lombok.Getter;
import lombok.NoArgsConstructor;

/** 방장 위임 요청 body: {"userId": 5} (새 방장이 될 참가자의 userId) */
@Getter
@NoArgsConstructor
public class TransferHostRequestDto {
    private Long userId;
}
