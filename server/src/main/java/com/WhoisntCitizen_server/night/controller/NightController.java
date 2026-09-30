package com.WhoisntCitizen_server.night.controller;

import com.WhoisntCitizen_server.night.dto.NightActionRequest;
import com.WhoisntCitizen_server.night.dto.NightActionResponse;
import com.WhoisntCitizen_server.night.dto.NightResultResponse;
import com.WhoisntCitizen_server.night.service.NightService;
import jakarta.validation.Valid;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestHeader;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/games/{gameId}")
public class NightController {

    private final NightService nightService;

    public NightController(NightService nightService) {
        this.nightService = nightService;
    }

    /** 3. 밤 능력 사용 (NIGHT 페이즈에서만) */
    @PostMapping("/night-actions")
    public NightActionResponse submitNightAction(@PathVariable String gameId,
                                                 @RequestHeader("X-Player-Id") Long playerId,
                                                 @Valid @RequestBody NightActionRequest request) {
        return nightService.submitAction(gameId, playerId, request.targetId());
    }

    /** 3. 이번 밤 능력을 쓰지 않고 넘기기 (NIGHT 페이즈에서만). 요청 본문 없음 */
    @PostMapping("/night-actions/skip")
    public NightActionResponse skipNightAction(@PathVariable String gameId,
                                               @RequestHeader("X-Player-Id") Long playerId) {
        return nightService.skipAction(gameId, playerId);
    }

    /** 4. 밤 결과 공개 */
    @GetMapping("/night-result")
    public NightResultResponse nightResult(@PathVariable String gameId,
                                           @RequestHeader("X-Player-Id") Long playerId) {
        return nightService.getNightResult(gameId, playerId);
    }
}
