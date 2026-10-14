package com.WhoisntCitizen_server.chat.controller;

import com.WhoisntCitizen_server.chat.dto.ChatMessageResponse;
import com.WhoisntCitizen_server.chat.dto.ChatMessageSummary;
import com.WhoisntCitizen_server.chat.dto.SendMessageRequest;
import com.WhoisntCitizen_server.chat.service.ChatMessageService;
import jakarta.validation.Valid;
import org.springframework.http.HttpStatus;
import org.springframework.security.core.annotation.AuthenticationPrincipal;
import org.springframework.security.oauth2.jwt.Jwt;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

import java.util.List;

/**
 * 로비 채팅 API (v1). 로비 전체가 방 하나처럼 동작합니다. Authorization: Bearer {accessToken} 필요.
 *  - 조회: GET  /api/v1/lobby/messages[?limit=50][&afterId=100]   200 OK
 *          방 채팅 조회와 같은 형식. 클라이언트는 로비에 들어올 때 limit=1로 마지막 id를 받아 두고, 그 뒤로는 afterId로 새 메시지만 받습니다.
 *  - 전송: POST /api/v1/lobby/messages  {"message":"..."}
 *          201 Created (방 채팅 전송 응답과 같은 형식, 3번째 같은 내용부터 spamWarning)
 *          400 프로필 없음 / 429 CHAT_MUTED 도배로 채팅 금지
 */
@RestController
@RequestMapping("/api/v1/lobby")
public class LobbyChatController {

    private final ChatMessageService service;

    public LobbyChatController(ChatMessageService service) {
        this.service = service;
    }

    @GetMapping("/messages")
    public List<ChatMessageSummary> getMessages(@RequestParam(required = false) Long afterId,
                                                @RequestParam(required = false) Integer limit) {
        return service.getLobbyMessages(afterId, limit);
    }

    @PostMapping("/messages")
    @ResponseStatus(HttpStatus.CREATED)
    public ChatMessageResponse send(@AuthenticationPrincipal Jwt jwt,
                                    @Valid @RequestBody SendMessageRequest request) {
        return service.sendLobby(Long.valueOf(jwt.getSubject()), request.message());
    }
}
