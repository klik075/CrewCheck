package com.WhoisntCitizen_server.member.service;

import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.context.event.EventListener;
import org.springframework.stereotype.Component;

/**
 * 9. 게임 종료 → 회원 전적 저장.
 * GameEndedEvent를 받아 UserStatsService(트랜잭션)에 위임한다.
 */
@Slf4j
@Component
@RequiredArgsConstructor
public class UserStatsListener {

    private final UserStatsService userStatsService;

    @EventListener
    public void onGameEnded(GameEndedEvent event) {
        if (!event.recordStats()) {
            // 개발용 테스트 게임(POST /api/v1/games)은 전적에 반영하지 않는다.
            log.debug("[{}] 전적 미반영 게임", event.gameId());
            return;
        }
        // 이 리스너가 실패해도 다른 리스너(방 복귀 등)에 영향이 없도록 여기서 처리한다.
        try {
            int updated = userStatsService.recordGameResult(event);
            log.info("[{}] 전적 저장 완료: {}명 (승리: {})", event.gameId(), updated, event.winner());
        } catch (RuntimeException e) {
            log.error("[{}] 전적 저장 실패", event.gameId(), e);
        }
    }
}
