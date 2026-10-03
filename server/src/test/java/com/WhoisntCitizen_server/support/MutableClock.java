package com.WhoisntCitizen_server.support;

import java.time.Clock;
import java.time.Duration;
import java.time.Instant;
import java.time.ZoneId;
import java.time.ZoneOffset;

/** 테스트용: 직접 옮길 수 있는 시계 */
public class MutableClock extends Clock {

    private Instant now;

    public MutableClock(Instant start) {
        this.now = start;
    }

    public void set(Instant instant) {
        this.now = instant;
    }

    public void advance(Duration duration) {
        this.now = now.plus(duration);
    }

    @Override
    public Instant instant() {
        return now;
    }

    @Override
    public ZoneId getZone() {
        return ZoneOffset.UTC;
    }

    @Override
    public Clock withZone(ZoneId zone) {
        return this;
    }
}
