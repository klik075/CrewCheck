package com.sparta.unityaitestproject_server.game.dto;

import com.sparta.unityaitestproject_server.game.entity.GamePhase;

import java.time.Instant;

public record StartGameResponse(String gameId, GamePhase phase, int day, Instant phaseEndsAt) {
}
