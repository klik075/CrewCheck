package com.WhoisntCitizen_server.game.repository;

import com.WhoisntCitizen_server.game.entity.Game;
import com.WhoisntCitizen_server.game.event.GameStateChangedEvent;
import com.WhoisntCitizen_server.game.lock.GameLockScope;
import org.springframework.context.ApplicationEventPublisher;

import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;

/**
 * 게임 저장소를 감싸서, 저장할 때마다 GameStateChangedEvent를 발행한다. (실제 저장은 감싼 저장소가 한다)
 *
 * 왜 저장소에서 하나
 *  게임 상태를 바꾸는 곳은 GameFlowService, GameService, NightService, VoteService 등 여러 곳이다.
 *  각자 "바뀌었다"를 알리게 하면 하나만 빠뜨려도 클라이언트가 변경을 모른다. 모든 변경은 결국 save()를 거치므로 여기서 한 번에 잡는다.
 *
 * 언제 발행하나
 *  - 게임 잠금 안에서 저장하면: 잠금이 풀린 뒤(GameLockScope.afterUnlock). 잠금을 쥔 채 알리는 일을 하지 않기 위해서다.
 *    클라이언트가 신호를 받고 GET을 보냈을 때 아직 잠금이 잡혀 있어 기다리는 일도 없다.
 *  - 같은 잠금 안에서 여러 번 저장하면(예: 투표 저장 → 바로 처형 판정 저장): 게임마다 한 번만, 마지막 상태로 발행한다.
 *  - 잠금 밖에서 저장하면(게임 생성): 바로 발행한다.
 *  작업이 예외로 끝나도 이미 저장된 변경은 알린다. (GameLockScope는 예외 때도 모아 둔 일을 실행한다)
 *
 * 삭제(delete)는 알리지 않는다. 끝난 게임 정리라 클라이언트는 이미 결과를 받았고, 방 복귀는 방 신호(room.changed)로 알린다.
 */
public class ChangeEventGameRepository implements GameRepository {

    private final GameRepository delegate;
    private final ApplicationEventPublisher eventPublisher;
    /** 이번 잠금 안에서 저장된 게임 → 발행할 이벤트 (스레드마다 따로) */
    private final ThreadLocal<Map<String, GameStateChangedEvent>> pending = new ThreadLocal<>();

    public ChangeEventGameRepository(GameRepository delegate, ApplicationEventPublisher eventPublisher) {
        this.delegate = Objects.requireNonNull(delegate, "delegate");
        this.eventPublisher = Objects.requireNonNull(eventPublisher, "eventPublisher");
    }

    /** 감싼 실제 저장소 (확인·테스트용) */
    public GameRepository delegate() {
        return delegate;
    }

    @Override
    public Game save(Game game) {
        Game saved = delegate.save(game);
        GameStateChangedEvent event = GameStateChangedEvent.of(saved);
        if (!GameLockScope.isHeld()) {
            eventPublisher.publishEvent(event);
            return saved;
        }
        Map<String, GameStateChangedEvent> events = pending.get();
        if (events == null) {
            Map<String, GameStateChangedEvent> created = new LinkedHashMap<>();
            pending.set(created);
            GameLockScope.afterUnlock(() -> {
                pending.remove();
                created.values().forEach(eventPublisher::publishEvent);
            });
            events = created;
        }
        events.put(event.gameId(), event); // 같은 게임을 다시 저장하면 마지막 상태로 덮는다
        return saved;
    }

    @Override
    public Optional<Game> findById(String gameId) {
        return delegate.findById(gameId);
    }

    @Override
    public List<String> findActiveIds() {
        return delegate.findActiveIds();
    }

    @Override
    public void delete(String gameId) {
        delegate.delete(gameId);
    }
}
