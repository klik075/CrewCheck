package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.common.config.GamePhaseProperties;
import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.scheduling.LocalGameTimer;
import com.WhoisntCitizen_server.game.scheduling.SchedulerDeferredEventPublisher;
import com.WhoisntCitizen_server.game.service.GameFlowService;
import com.WhoisntCitizen_server.game.service.WinConditionChecker;
import com.WhoisntCitizen_server.night.service.NightActionResolver;
import com.WhoisntCitizen_server.vote.service.VoteResolver;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.scheduling.TaskScheduler;

import java.time.Clock;

/**
 * 테스트용 GameFlowService 생성. 예전 생성자(TaskScheduler를 직접 받던 형태)와 같은 인자로
 * GameTimer·DeferredEventPublisher를 그 스케줄러 위에 만들어 끼운다.
 * ManualTaskScheduler를 넘기면 타이머와 지연 이벤트가 예전과 같은 순서로 실행된다.
 */
public final class TestGameFlows {

    private TestGameFlows() {
    }

    public static GameFlowService create(GameRepository repository,
                                         NightActionResolver nightActionResolver,
                                         VoteResolver voteResolver,
                                         WinConditionChecker winConditionChecker,
                                         TaskScheduler scheduler,
                                         GamePhaseProperties props,
                                         Clock clock,
                                         ApplicationEventPublisher eventPublisher) {
        // 타이머가 호출할 대상(GameFlowService)은 아래에서 만들어지므로 배열로 나중에 채운다.
        GameFlowService[] flow = new GameFlowService[1];
        flow[0] = new GameFlowService(repository, nightActionResolver, voteResolver, winConditionChecker,
                new LocalGameTimer(scheduler, () -> flow[0]),
                new SchedulerDeferredEventPublisher(scheduler, clock, eventPublisher),
                props, clock, eventPublisher);
        return flow[0];
    }
}
