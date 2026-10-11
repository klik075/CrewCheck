package com.WhoisntCitizen_server.chat.event;

import java.util.Set;

/**
 * 방 채팅에 메시지가 저장됐다. (사용자 채팅, 사망자 채팅, 밤의 해적·세이렌 채팅, 시스템 안내)
 * ChatMessageService가 저장 직후 발행한다.
 *
 * 받는 쪽: 실시간 모듈(RealtimeChatListener)이 chat.changed를 보낸다.
 *  - audience == null: 모두에게 보이는 메시지 → 방 구독자 전원
 *  - audience != null: 일부에게만 보이는 메시지 → 그 사용자들에게만 (비어 있으면 아무에게도 보내지 않는다)
 *
 * 누가 볼 수 있는지는 채팅 조회(GET)와 같은 규칙으로, 저장하는 순간 게임 잠금 안에서 정한다.
 * 볼 수 없는 사람에게 신호를 보내면 "방금 나에게 안 보이는 메시지가 오갔다"는 사실(밤에 해적이 대화 중)이 새어 나가므로 보내지 않는다.
 *
 * @param audience 볼 수 있는 사용자 id들. 모두에게 보이면 null
 */
public record ChatMessageSavedEvent(long roomId, long messageId, Set<Long> audience) {

    public boolean visibleToEveryone() {
        return audience == null;
    }
}
