package com.WhoisntCitizen_server.chat.dto;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/**
 * POST /api/v1/rooms/{roomId}/messages 요청 본문: {"message":"2번이 마피아 같은데?"}
 * 보낸 사람은 로비와 같이 JWT(Authorization: Bearer ...)로 식별하므로 본문에 userId를 받지 않습니다.
 */
public record SendMessageRequest(
        @NotBlank(message = "message는 비어 있을 수 없습니다.")
        @Size(max = ChatMessage.MAX_MESSAGE_LENGTH, message = "message는 {max}자 이하여야 합니다.")
        String message
) {
}
