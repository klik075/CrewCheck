package com.WhoisntCitizen_server.game.service;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.common.exception.LockTimeoutException;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Qualifier;
import org.springframework.boot.context.event.ApplicationReadyEvent;
import org.springframework.context.event.EventListener;
import org.springframework.scheduling.TaskScheduler;
import org.springframework.stereotype.Component;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;

/**
 * 연결 끊김 검사. inactiveCheckSeconds마다 진행 중인 모든 게임(GameRepository.findActiveIds)을 확인해
 * 마지막 요청 후 inactiveTimeoutSeconds가 지난 플레이어를 GameFlowService.checkInactivePlayers로 처리한다.
 * 클라이언트는 게임 화면에서 1초마다 상태를 조회하므로, 상태 조회가 끊기면 연결이 끊긴 것으로 본다.
 *
 * [서버가 하나일때만 적용] 서버가 켜진 뒤 inactiveTimeoutSeconds만큼 기다렸다가 첫 검사를 한다. (재시작 직후 유예, start() 참고)
 * 대기실 쪽 RoomPresenceService.start와 같은 방식이다.
 */
@Component
public class InactivePlayerMonitor {

    private static final Logger log = LoggerFactory.getLogger(InactivePlayerMonitor.class);

    private final GameRepository gameRepository;
    private final GameFlowService gameFlowService;
    private final TaskScheduler scheduler;
    private final GamePhaseProperties phaseProps;
    // 서버가 하나일때: 재시작 직후 유예(첫 검사 시각)를 계산할 때 쓴다
    private final Clock clock;

    public InactivePlayerMonitor(GameRepository gameRepository,
                                 GameFlowService gameFlowService,
                                 @Qualifier("gamePhaseScheduler") TaskScheduler scheduler,
                                 GamePhaseProperties phaseProps,
                                 Clock clock) {
        this.gameRepository = gameRepository;
        this.gameFlowService = gameFlowService;
        this.scheduler = scheduler;
        this.phaseProps = phaseProps;
        this.clock = clock;
    }

    /**
     * 서버가 하나일때만 적용 (재시작 직후 유예)
     *
     * 서버가 요청을 받을 준비가 되면 검사를 예약한다. 첫 검사는 지금부터 inactiveTimeoutSeconds 뒤다.
     *
     * 왜 기다리나
     *  접속 기록은 Redis에 남아 있으므로, 서버가 꺼져 있던 동안(죽은 뒤 자동 재시작 5초 + 시작 시간, 또는 배포 중)
     *  모든 참가자의 마지막 기록이 서버가 꺼진 시각에 멈춰 있다. 켜지자마자 검사하면 꺼져 있던 시간이 길 때
     *  클라이언트가 다시 붙기도 전에 전원이 기준(60초)을 넘긴 것으로 보여 게임이 취소된다. (CANCELLED_ALL_DISCONNECTED)
     *  기준 시간만큼 기다리는 동안 살아 있는 클라이언트가 상태 조회(1초 폴링)·재구독·ping으로 기록을 갱신한다.
     *  그 뒤에도 기록이 없는 사람은 정말 떠난 사람이므로 그때부터 원래대로 처리한다.
     *
     * 서버가 여러 대이면
     *  다른 서버가 검사를 계속 돌리므로 이 유예는 "막 켜진 서버 한 대의 첫 검사를 늦추는 것"일 뿐 결과에 영향이 거의 없다.
     *  (모든 서버가 동시에 꺼졌다 켜진 경우에만 서버 1대일 때와 같은 효과가 난다)
     *
     * 대가: 서버가 켜진 직후 60초 동안은 정말로 앱을 끈 사람도 처리되지 않는다. (최대 60초 늦게 처리)
     */
    @EventListener(ApplicationReadyEvent.class)
    public void start() {
        if (phaseProps.inactiveTimeoutSeconds() <= 0 || phaseProps.inactiveCheckSeconds() <= 0) {
            log.info("연결 끊김 검사 꺼짐 (mafia.phase.inactive-timeout-seconds={})", phaseProps.inactiveTimeoutSeconds());
            return;
        }
        Instant firstCheck = clock.instant().plusSeconds(phaseProps.inactiveTimeoutSeconds());
        scheduler.scheduleWithFixedDelay(this::checkAll, firstCheck, Duration.ofSeconds(phaseProps.inactiveCheckSeconds()));
        log.info("연결 끊김 검사 시작: {}초 뒤부터 {}초마다, {}초 동안 요청이 없으면 이탈 처리 (재시작 직후 유예)",
                phaseProps.inactiveTimeoutSeconds(), phaseProps.inactiveCheckSeconds(), phaseProps.inactiveTimeoutSeconds());
    }

    /** 반복 작업에서 예외가 밖으로 나가면 이후 검사가 멈추므로 게임마다 예외를 잡는다. */
    void checkAll() {
        // id만 받고, 게임은 checkInactivePlayers가 게임 잠금 안에서 다시 읽는다 (그 사이 끝났거나 삭제됐으면 건너뜀)
        for (String gameId : gameRepository.findActiveIds()) {
            try {
                gameFlowService.checkInactivePlayers(gameId);
            } catch (LockTimeoutException e) {
                // 게임이 바빠 이번 검사를 건너뜀. 다음 검사(inactiveCheckSeconds 뒤)에서 다시 확인한다
                log.debug("[{}] 게임 잠금이 바빠 이번 연결 끊김 검사를 건너뜁니다", gameId);
            } catch (RuntimeException e) {
                log.error("[{}] 연결 끊김 검사 실패", gameId, e);
            }
        }
    }
}
