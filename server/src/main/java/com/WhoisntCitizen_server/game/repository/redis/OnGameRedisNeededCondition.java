package com.WhoisntCitizen_server.game.repository.redis;

import com.WhoisntCitizen_server.common.servermode.ServerSetting;
import com.WhoisntCitizen_server.common.servermode.ServerSettings;
import org.springframework.boot.autoconfigure.condition.ConditionMessage;
import org.springframework.boot.autoconfigure.condition.ConditionOutcome;
import org.springframework.boot.autoconfigure.condition.SpringBootCondition;
import org.springframework.context.annotation.ConditionContext;
import org.springframework.core.type.AnnotatedTypeMetadata;

import java.util.List;

/**
 * 게임 그룹 Redis 연결(GameRedis)이 필요한지 판단한다.
 * 게임 상태 저장소(mafia.game.repository)나 게임 타이머(mafia.game.timer) 중 하나라도 최종 값이 redis면 필요하다.
 * (게임 잠금은 Redisson 연결을 따로 만들므로 여기에 넣지 않는다)
 */
class OnGameRedisNeededCondition extends SpringBootCondition {

    /** GameRedis 연결을 쓰는 설정 */
    static final List<ServerSetting> USERS = List.of(ServerSetting.GAME_REPOSITORY, ServerSetting.GAME_TIMER);

    @Override
    public ConditionOutcome getMatchOutcome(ConditionContext context, AnnotatedTypeMetadata metadata) {
        ConditionMessage.Builder message = ConditionMessage.forCondition("게임 그룹 Redis 연결");
        for (ServerSetting setting : USERS) {
            String value = ServerSettings.resolve(context.getEnvironment(), setting);
            if (setting.isShared(value)) {
                return ConditionOutcome.match(message.because(setting.key() + "=" + value));
            }
        }
        return ConditionOutcome.noMatch(message.because("게임 저장소·게임 타이머가 모두 서버 메모리 방식"));
    }
}
