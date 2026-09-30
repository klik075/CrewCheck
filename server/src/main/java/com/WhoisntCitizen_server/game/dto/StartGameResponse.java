package com.WhoisntCitizen_server.game.dto;

import com.WhoisntCitizen_server.game.entity.GamePhase;

import java.time.Instant;

public record StartGameResponse(String gameId, GamePhase phase, int day, Instant phaseEndsAt) {
}
