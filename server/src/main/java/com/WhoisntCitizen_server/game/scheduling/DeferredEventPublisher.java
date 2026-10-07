package com.WhoisntCitizen_server.game.scheduling;

/**
 * 게임 잠금이 풀린 뒤에 처리돼야 하는 이벤트 발행. (게임 종료, 연결 끊김 등 로비·회원 모듈이 받는 이벤트)
 * 받는 쪽이 방 잠금이나 DB 트랜잭션을 잡으므로, 게임 잠금을 쥔 채로 바로 처리하면
 * "게임 잠금 → 방 잠금"과 "방 잠금 → 게임 잠금"이 엇갈려 교착 상태가 생길 수 있다.
 * event는 잠금 안에서 만든 값(현재 상태의 복사본)이어야 한다.
 */
public interface DeferredEventPublisher {

    void publishAfterLock(Object event);
}
