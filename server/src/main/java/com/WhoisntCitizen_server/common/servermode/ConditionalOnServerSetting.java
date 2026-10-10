package com.WhoisntCitizen_server.common.servermode;

import org.springframework.context.annotation.Conditional;

import java.lang.annotation.Documented;
import java.lang.annotation.ElementType;
import java.lang.annotation.Retention;
import java.lang.annotation.RetentionPolicy;
import java.lang.annotation.Target;

/**
 * 설정 항목의 "최종 값"이 havingValue일 때만 Bean을 등록한다.
 * 최종 값 = 개별 설정에 값이 있으면 그 값, 비어 있으면 mafia.server.mode의 기본값. (ServerSettings.resolve)
 *
 * @ConditionalOnProperty는 설정 하나만 볼 수 있어서, "비어 있으면 모드를 따른다"를 표현하지 못해 따로 만들었다.
 * 예) @ConditionalOnServerSetting(value = ServerSetting.GAME_LOCK, havingValue = "redis")
 */
@Target({ElementType.TYPE, ElementType.METHOD})
@Retention(RetentionPolicy.RUNTIME)
@Documented
@Conditional(OnServerSettingCondition.class)
public @interface ConditionalOnServerSetting {

    ServerSetting value();

    String havingValue();
}
