package com.WhoisntCitizen_server.chat.dto;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.Size;

/** POST /api/v1/rooms/{roomId}/system-messages 요청 본문: {"message":"밤이 되었습니다."} */
public record SystemMessageRequest(
        @NotBlank(message = "message는 비어 있을 수 없습니다.")
        @Size(max = ChatMessage.MAX_MESSAGE_LENGTH, message = "message는 {max}자 이하여야 합니다.")
        String message
) {
}
