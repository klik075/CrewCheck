package com.WhoisntCitizen_server.common.server;

import org.junit.jupiter.api.Test;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/** 서버 ID를 정하는 규칙. (Spring 없이) */
class ServerIdentityTest {

    @Test
    void SERVER_ID가_있으면_그_값을_쓴다() {
        ServerIdentity identity = ServerIdentity.resolve(" server-a ");

        assertThat(identity.id()).isEqualTo("server-a");
        assertThat(identity.configured()).isTrue();
    }

    @Test
    void 비어_있으면_호스트_이름과_임의값으로_만든다() {
        ServerIdentity first = ServerIdentity.resolve("");
        ServerIdentity second = ServerIdentity.resolve(null);

        assertThat(first.configured()).isFalse();
        assertThat(first.id()).matches("[A-Za-z0-9._-]+-[0-9a-f]{8}");
        assertThat(first.id()).isNotEqualTo(second.id()); // 같은 컴퓨터에서 두 대를 띄워도 겹치지 않는다
    }

    @Test
    void 같은_ID라도_프로세스마다_토큰이_다르다() {
        assertThat(ServerIdentity.resolve("server-a").instanceToken())
                .isNotEqualTo(ServerIdentity.resolve("server-a").instanceToken());
    }

    @Test
    void 쓸_수_없는_글자나_너무_긴_값은_막는다() {
        assertThatThrownBy(() -> ServerIdentity.resolve("server a"))
                .isInstanceOf(IllegalStateException.class)
                .hasMessageContaining("SERVER_ID");
        assertThatThrownBy(() -> ServerIdentity.resolve("서버1"))
                .isInstanceOf(IllegalStateException.class);
        assertThatThrownBy(() -> ServerIdentity.resolve("a".repeat(65)))
                .isInstanceOf(IllegalStateException.class);
    }
}
