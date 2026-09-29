package com.sparta.unityaitestproject_server.night.dto;

import jakarta.validation.constraints.NotNull;

public record NightActionRequest(@NotNull Long targetId) {
}
