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
import com.WhoisntCitizen_server.user.service.CurrentUserResolver;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/v1/games/{gameId}")
public class VoteController {

    private final VoteService voteService;
    private final CurrentUserResolver currentUser;

    public VoteController(VoteService voteService, CurrentUserResolver currentUser) {
        this.voteService = voteService;
        this.currentUser = currentUser;
    }

    /** 6. 투표 (VOTE 페이즈에서만). targetId=null이면 기권, confirm=false면 임시 선택(시간 종료 시 집계) */
    @PostMapping("/votes")
    public VoteResponse vote(@PathVariable String gameId,
                             @AuthenticationPrincipal Jwt jwt,
                             @Valid @RequestBody VoteRequest request) {
        return voteService.vote(gameId, currentUser.userId(jwt), request.targetId(), request.confirmed());
    }

    /** 7. 처형 결과 */
    @GetMapping("/execution-result")
    public ExecutionResultResponse executionResult(@PathVariable String gameId) {
        return voteService.getExecutionResult(gameId);
    }
}
