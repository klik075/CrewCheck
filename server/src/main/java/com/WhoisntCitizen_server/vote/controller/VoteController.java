package com.WhoisntCitizen_server.vote.controller;

import com.WhoisntCitizen_server.vote.dto.ExecutionResultResponse;
import com.WhoisntCitizen_server.vote.dto.VoteRequest;
import com.WhoisntCitizen_server.vote.dto.VoteResponse;
import com.WhoisntCitizen_server.vote.service.VoteService;
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
public class VoteController {

    private final VoteService voteService;

    public VoteController(VoteService voteService) {
        this.voteService = voteService;
    }

    /** 6. 투표 (VOTE 페이즈에서만) */
    @PostMapping("/votes")
    public VoteResponse vote(@PathVariable String gameId,
                             @RequestHeader("X-Player-Id") Long playerId,
                             @Valid @RequestBody VoteRequest request) {
        return voteService.vote(gameId, playerId, request.targetId());
    }

    /** 7. 처형 결과 */
    @GetMapping("/execution-result")
    public ExecutionResultResponse executionResult(@PathVariable String gameId) {
        return voteService.getExecutionResult(gameId);
    }
}
