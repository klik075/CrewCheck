package com.WhoisntCitizen_server.realtime;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.OptionalLong;
import java.util.Set;

/**
 * 이 서버에 붙어 있는 연결 목록. (서버 메모리)
 *
 *  - 연결 id → (연결, 구독 중인 방)
 *  - 사용자 id → 연결 id들   (한 사용자가 연결을 여러 개 가질 수 있다: 재연결이 겹치는 순간, 두 기기)
 *  - 방 id    → 연결 id들
 *
 * 규칙
 *  - 연결 하나는 방 하나만 구독한다. 다른 방을 구독하면 이전 방에서 빠진다
 *  - 연결이 끊기면 모든 목록에서 빠진다
 *
 * 연결 목록은 서버 여러 대가 되어도 서버마다 따로다. (WebSocket 연결은 그 서버에만 있으므로)
 * 다른 서버의 연결에 보내는 일은 5단계에서 RealtimeNotifier가 Redis Pub/Sub으로 한다.
 *
 * 바꾸는 일(연결·구독)은 자주 일어나지 않아 메서드 전체를 synchronized로 단순하게 묶었다.
 * 조회는 복사본을 돌려주므로, 실제 보내기는 잠금 밖에서 한다.
 */
public class RealtimeSessionRegistry {

    private record Entry(RealtimeConnection connection, Long roomId) {
        Entry withRoom(Long newRoomId) {
            return new Entry(connection, newRoomId);
        }
    }

    private final Map<String, Entry> byId = new HashMap<>();
    private final Map<Long, Set<String>> byUser = new HashMap<>();
    private final Map<Long, Set<String>> byRoom = new HashMap<>();

    public synchronized void register(RealtimeConnection connection) {
        unregister(connection.id()); // 같은 id로 다시 등록되면 이전 것을 정리한다
        byId.put(connection.id(), new Entry(connection, null));
        byUser.computeIfAbsent(connection.userId(), k -> new LinkedHashSet<>()).add(connection.id());
    }

    public synchronized void unregister(String connectionId) {
        Entry entry = byId.remove(connectionId);
        if (entry == null) {
            return;
        }
        removeFrom(byUser, entry.connection().userId(), connectionId);
        if (entry.roomId() != null) {
            removeFrom(byRoom, entry.roomId(), connectionId);
        }
    }

    /**
     * 연결이 방을 구독한다. 이미 다른 방을 구독 중이면 그 방에서 빠진다.
     * @return 등록되지 않은 연결이면 false
     */
    public synchronized boolean subscribe(String connectionId, long roomId) {
        Entry entry = byId.get(connectionId);
        if (entry == null) {
            return false;
        }
        if (entry.roomId() != null && entry.roomId() != roomId) {
            removeFrom(byRoom, entry.roomId(), connectionId);
        }
        byId.put(connectionId, entry.withRoom(roomId));
        byRoom.computeIfAbsent(roomId, k -> new LinkedHashSet<>()).add(connectionId);
        return true;
    }

    /** 그 방을 구독 중일 때만 푼다. (늦게 도착한 해제 요청이 새로 구독한 방을 풀지 않게) */
    public synchronized void unsubscribe(String connectionId, long roomId) {
        Entry entry = byId.get(connectionId);
        if (entry == null || entry.roomId() == null || entry.roomId() != roomId) {
            return;
        }
        byId.put(connectionId, entry.withRoom(null));
        removeFrom(byRoom, roomId, connectionId);
    }

    /**
     * 사용자의 모든 연결을 그 방 구독에서 뺀다. 방에서 나가거나 강퇴됐을 때 쓴다. (4-5)
     * @return 구독을 푼 연결 수
     */
    public synchronized int removeUserFromRoom(long roomId, long userId) {
        int removed = 0;
        for (String connectionId : List.copyOf(byUser.getOrDefault(userId, Set.of()))) {
            Entry entry = byId.get(connectionId);
            if (entry != null && entry.roomId() != null && entry.roomId() == roomId) {
                unsubscribe(connectionId, roomId);
                removed++;
            }
        }
        return removed;
    }

    /**
     * 그 방의 구독을 모두 푼다. 방이 삭제됐을 때 쓴다. (연결 자체는 유지)
     * @return 구독을 푼 연결 수
     */
    public synchronized int removeRoom(long roomId) {
        Set<String> ids = byRoom.remove(roomId);
        if (ids == null) {
            return 0;
        }
        for (String id : ids) {
            Entry entry = byId.get(id);
            if (entry != null) {
                byId.put(id, entry.withRoom(null));
            }
        }
        return ids.size();
    }

    /** 방을 구독 중인 연결들 (복사본) */
    public synchronized List<RealtimeConnection> connectionsOfRoom(long roomId) {
        return connections(byRoom.get(roomId));
    }

    /** 사용자의 연결들 (복사본) */
    public synchronized List<RealtimeConnection> connectionsOfUser(long userId) {
        return connections(byUser.get(userId));
    }

    /** 연결이 구독 중인 방 */
    public synchronized OptionalLong roomOf(String connectionId) {
        Entry entry = byId.get(connectionId);
        return entry == null || entry.roomId() == null ? OptionalLong.empty() : OptionalLong.of(entry.roomId());
    }

    public synchronized int connectionCount() {
        return byId.size();
    }

    private List<RealtimeConnection> connections(Set<String> ids) {
        if (ids == null || ids.isEmpty()) {
            return List.of();
        }
        List<RealtimeConnection> result = new ArrayList<>(ids.size());
        for (String id : ids) {
            Entry entry = byId.get(id);
            if (entry != null) {
                result.add(entry.connection());
            }
        }
        return result;
    }

    private static void removeFrom(Map<Long, Set<String>> index, long key, String connectionId) {
        Set<String> ids = index.get(key);
        if (ids == null) {
            return;
        }
        ids.remove(connectionId);
        if (ids.isEmpty()) {
            index.remove(key); // 빈 목록이 계속 쌓이지 않게
        }
    }
}
