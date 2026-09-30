package com.WhoisntCitizen_server.vote.dto;

import jakarta.validation.constraints.NotNull;

public record VoteRequest(@NotNull Long targetId) {
}
