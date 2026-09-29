package com.sparta.unityaitestproject_server.vote.dto;

import jakarta.validation.constraints.NotNull;

public record VoteRequest(@NotNull Long targetId) {
}
