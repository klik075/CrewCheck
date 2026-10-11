package com.WhoisntCitizen_server.common.servermode;

import org.springframework.boot.autoconfigure.condition.ConditionMessage;
import org.springframework.boot.autoconfigure.condition.ConditionOutcome;
import org.springframework.boot.autoconfigure.condition.SpringBootCondition;
import org.springframework.context.annotation.ConditionContext;
import org.springframework.core.type.AnnotatedTypeMetadata;

import java.util.Map;

/**
 * @ConditionalOnServerSetting을 실제로 판단한다.
 * SpringBootCondition을 쓰면 --debug 실행 때 나오는 조건 보고서에 왜 등록됐는지/안 됐는지가 함께 찍힌다.
 */
class OnServerSettingCondition extends SpringBootCondition {

    @Override
    public ConditionOutcome getMatchOutcome(ConditionContext context, AnnotatedTypeMetadata metadata) {
        Map<String, Object> attributes = metadata.getAnnotationAttributes(ConditionalOnServerSetting.class.getName());
        if (attributes == null) {
            return ConditionOutcome.noMatch("@ConditionalOnServerSetting이 없습니다");
        }
        ServerSetting setting = toSetting(attributes.get("value"));
        String expected = (String) attributes.get("havingValue");
        String actual = ServerSettings.resolve(context.getEnvironment(), setting);

        ConditionMessage.Builder message = ConditionMessage.forCondition(ConditionalOnServerSetting.class,
                setting.key() + "=" + expected);
        String reason = "최종 값이 " + actual + " (" + ServerMode.PROPERTY + "="
                + ServerSettings.mode(context.getEnvironment()).value() + ")";
        return actual.equals(expected)
                ? ConditionOutcome.match(message.because(reason))
                : ConditionOutcome.noMatch(message.because(reason));
    }

    /** 컴포넌트 스캔(ASM)으로 읽으면 enum 대신 이름 문자열이 올 수도 있어 둘 다 받는다. */
    private static ServerSetting toSetting(Object value) {
        if (value instanceof ServerSetting setting) {
            return setting;
        }
        return ServerSetting.valueOf(String.valueOf(value));
    }
}
