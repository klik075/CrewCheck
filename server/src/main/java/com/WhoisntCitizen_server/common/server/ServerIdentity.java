package com.WhoisntCitizen_server.common.server;

import java.net.InetAddress;
import java.util.Objects;
import java.util.UUID;
import java.util.regex.Pattern;

/**
 * 이 서버 프로세스의 이름표. (멀티서버 2단계)
 *
 * 서버를 여러 대 띄우면 "이 로그·이 신호·이 응답이 어느 서버에서 나왔는지"를 알아야 한다. 그 값을 한 곳에서 정해 함께 쓴다.
 *  - Redis Pub/Sub 실시간 신호의 origin (자기 신호를 가려낸다, RedisRealtimeNotifier)
 *  - HTTP 응답 헤더 X-Server-Id (어느 서버가 처리했는지, ServerIdHeaderFilter)
 *  - 같은 ID를 쓰는 서버가 또 있는지 확인 (ServerInstanceRegistry)
 *
 * id 정하는 규칙 (resolve)
 *  1) mafia.server.id(환경변수 SERVER_ID)에 값이 있으면 그 값. 운영에서는 server-a, server-b처럼 붙인다
 *  2) 비어 있으면 "호스트 이름-임의 8자리". 켤 때마다 바뀐다. 로컬 개발·설정을 깜빡한 경우에도 서버끼리 겹치지 않게
 *
 * instanceToken은 이 프로세스 하나만의 값이다. id가 같아도(설정 실수) 프로세스가 다르면 다르다.
 * ServerInstanceRegistry가 "같은 id를 다른 프로세스가 쓰고 있는지"를 이 값으로 가려낸다.
 */
public final class ServerIdentity {

    /** 로그·헤더·Redis 키에 그대로 들어가므로 안전한 글자만 받는다 */
    private static final Pattern ALLOWED = Pattern.compile("[A-Za-z0-9._-]{1,64}");

    private final String id;
    private final boolean configured;
    private final String instanceToken;

    private ServerIdentity(String id, boolean configured, String instanceToken) {
        this.id = Objects.requireNonNull(id, "id");
        this.configured = configured;
        this.instanceToken = Objects.requireNonNull(instanceToken, "instanceToken");
    }

    /**
     * @param configuredId mafia.server.id 값. null·빈 문자열이면 자동으로 만든다
     * @throws IllegalStateException 허용하지 않는 글자가 있거나 64자를 넘을 때 (서버가 뜨기 전에 막는다)
     */
    public static ServerIdentity resolve(String configuredId) {
        String token = UUID.randomUUID().toString();
        if (configuredId == null || configuredId.isBlank()) {
            return new ServerIdentity(hostName() + "-" + token.substring(0, 8), false, token);
        }
        String id = configuredId.trim();
        if (!ALLOWED.matcher(id).matches()) {
            throw new IllegalStateException("mafia.server.id(SERVER_ID) 값 '" + configuredId
                    + "'을(를) 쓸 수 없습니다. 영문·숫자·'.'·'_'·'-'로 64자 이내여야 합니다. (예: server-a)");
        }
        return new ServerIdentity(id, true, token);
    }

    /** 서버 ID. 예) server-a, ip-172-31-5-9-3fa2c1d8 */
    public String id() {
        return id;
    }

    /** SERVER_ID로 직접 정했으면 true, 자동으로 만들었으면 false */
    public boolean configured() {
        return configured;
    }

    /** 이 프로세스만의 임의 값. 같은 id를 쓰는 다른 프로세스와 구별할 때 쓴다 */
    public String instanceToken() {
        return instanceToken;
    }

    @Override
    public String toString() {
        return id + (configured ? "" : " (자동 생성)");
    }

    private static String hostName() {
        try {
            String host = InetAddress.getLocalHost().getHostName();
            // 호스트 이름에 허용하지 않는 글자가 섞여 있을 수 있어 바꿔 둔다 (Windows 컴퓨터 이름 등)
            String safe = host.replaceAll("[^A-Za-z0-9._-]", "-");
            return safe.isBlank() ? "server" : safe.substring(0, Math.min(safe.length(), 40));
        } catch (Exception e) {
            return "server";
        }
    }
}
