package com.WhoisntCitizen_server.game.activity;

import com.WhoisntCitizen_server.support.PlayerActivityTrackerContractTest;

/** 서버 메모리 접속 기록이 공통 계약을 지키는지 확인한다. */
class LocalPlayerActivityTrackerTest extends PlayerActivityTrackerContractTest {

    @Override
    protected PlayerActivityTracker newTracker() {
        return new LocalPlayerActivityTracker();
    }
}
