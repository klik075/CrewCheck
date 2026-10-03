package com.WhoisntCitizen_server.support;

import org.springframework.scheduling.TaskScheduler;
import org.springframework.scheduling.Trigger;

import java.time.Duration;
import java.time.Instant;
import java.util.Comparator;
import java.util.PriorityQueue;
import java.util.concurrent.ScheduledFuture;

/**
 * 테스트용 스케줄러: 예약만 해 두고, advance로 시계를 옮길 때 예약 시각 순서대로 같은 스레드에서 실행한다.
 * 작업을 실행할 때는 시계를 그 작업의 예약 시각으로 맞추므로, 실행 중 새로 예약한 타이머도 실제와 같은 시각에 잡힌다.
 */
public class ManualTaskScheduler implements TaskScheduler {

    private record Task(Instant at, long seq, Runnable runnable) {
    }

    private final MutableClock clock;
    private final PriorityQueue<Task> tasks =
            new PriorityQueue<>(Comparator.comparing(Task::at).thenComparingLong(Task::seq));
    private long seq;

    public ManualTaskScheduler(MutableClock clock) {
        this.clock = clock;
    }

    /** 시계를 duration만큼 옮기며 그사이 예약된 작업(실행 중 새로 예약된 작업 포함)을 모두 실행한다. */
    public void advance(Duration duration) {
        Instant target = clock.instant().plus(duration);
        while (!tasks.isEmpty() && !tasks.peek().at().isAfter(target)) {
            Task task = tasks.poll();
            if (task.at().isAfter(clock.instant())) {
                clock.set(task.at());
            }
            task.runnable().run();
        }
        clock.set(target);
    }

    /** 시계를 옮기지 않고 지금까지 예약된 작업만 실행한다. (바로 발행하는 이벤트 등) */
    public void runDue() {
        advance(Duration.ZERO);
    }

    @Override
    public ScheduledFuture<?> schedule(Runnable task, Instant startTime) {
        tasks.add(new Task(startTime, seq++, task));
        return null;
    }

    @Override
    public ScheduledFuture<?> schedule(Runnable task, Trigger trigger) {
        throw new UnsupportedOperationException();
    }

    @Override
    public ScheduledFuture<?> scheduleAtFixedRate(Runnable task, Instant startTime, Duration period) {
        throw new UnsupportedOperationException();
    }

    @Override
    public ScheduledFuture<?> scheduleAtFixedRate(Runnable task, Duration period) {
        throw new UnsupportedOperationException();
    }

    @Override
    public ScheduledFuture<?> scheduleWithFixedDelay(Runnable task, Instant startTime, Duration delay) {
        throw new UnsupportedOperationException();
    }

    @Override
    public ScheduledFuture<?> scheduleWithFixedDelay(Runnable task, Duration delay) {
        throw new UnsupportedOperationException();
    }
}
