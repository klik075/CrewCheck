package com.WhoisntCitizen_server.night.dto;

import jakarta.validation.constraints.NotNull;

public record NightActionRequest(@NotNull Long targetId) {
}
