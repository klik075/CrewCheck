package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.event.EventListener;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.stereotype.Component;

import java.time.Duration;

/**
 * 연결 끊김 검사. inactiveCheckSeconds마다 진행 중인 모든 게임을 확인해
 * 마지막 요청 후 inactiveTimeoutSeconds가 지난 플레이어를 GameFlowService.checkInactivePlayers로 처리한다.
 * 클라이언트는 게임 화면에서 1초마다 상태를 조회하므로, 상태 조회가 끊기면 연결이 끊긴 것으로 본다.
 */
@Component
public class InactivePlayerMonitor {

    private static final Logger log = LoggerFactory.getLogger(InactivePlayerMonitor.class);

    private final GameRepository gameRepository;
    private final GameFlowService gameFlowService;
    private final TaskScheduler scheduler;
    private final GamePhaseProperties phaseProps;

    public InactivePlayerMonitor(GameRepository gameRepository,
                                 GameFlowService gameFlowService,
                                 @Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                                 GamePhaseProperties phaseProps) {
        this.gameRepository = gameRepository;
        this.gameFlowService = gameFlowService;
        this.scheduler = scheduler;
        this.phaseProps = phaseProps;
    }

    @EventListener(ApplicationReadyEvent.class)
    public void start() {
        if (phaseProps.inactiveTimeoutSeconds() <= 0 || phaseProps.inactiveCheckSeconds() <= 0) {
            log.info("연결 끊김 검사 꺼짐 (mafia.phase.inactive-timeout-seconds={})", phaseProps.inactiveTimeoutSeconds());
            return;
        }
        scheduler.scheduleWithFixedDelay(this::checkAll, Duration.ofSeconds(phaseProps.inactiveCheckSeconds()));
        log.info("연결 끊김 검사 시작: {}초마다, {}초 동안 요청이 없으면 이탈 처리",
                phaseProps.inactiveCheckSeconds(), phaseProps.inactiveTimeoutSeconds());
    }

    /** 반복 작업에서 예외가 밖으로 나가면 이후 검사가 멈추므로 게임마다 예외를 잡는다. */
    void checkAll() {
        for (Game game : gameRepository.findAll()) {
            try {
                gameFlowService.checkInactivePlayers(game.getGameId());
            } catch (RuntimeException e) {
                log.error("[{}] 연결 끊김 검사 실패", game.getGameId(), e);
            }
        }
    }
}
