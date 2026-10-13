package com.WhoisntCitizen_server.chat;

import com.WhoisntCitizen_server.chat.entity.ChatMessage;
import com.WhoisntCitizen_server.chat.entity.MessageType;
import com.WhoisntCitizen_server.chat.repository.ChatMessageRepository;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicLong;

/** 테스트용 메모리 메시지 저장소 (Redis 없이 동작 확인) */
class InMemoryChatMessageRepository implements ChatMessageRepository {

    private final Map<Long, List<ChatMessage>> rooms = new ConcurrentHashMap<>();
    private final Map<Long, AtomicLong> seqs = new ConcurrentHashMap<>();
    private final Map<Long, Long> gameStarts = new ConcurrentHashMap<>(); // 게임 시작 때의 마지막 메시지 id

    @Override
    public synchronized ChatMessage save(long roomId, MessageType type, Long userId, String nickname, String message,
                                         String gameId, boolean pirateOnly, boolean sirenOnly) {
        long id = seqs.computeIfAbsent(roomId, k -> new AtomicLong()).incrementAndGet();
        ChatMessage m = new ChatMessage(id, roomId, type, userId, nickname, message, LocalDateTime.now(), gameId,
                pirateOnly, sirenOnly);
        rooms.computeIfAbsent(roomId, k -> new ArrayList<>()).add(m);
        return m;
    }

    @Override
    public synchronized List<ChatMessage> findLatest(long roomId, int limit) {
        List<ChatMessage> all = rooms.getOrDefault(roomId, List.of());
        return new ArrayList<>(all.subList(Math.max(0, all.size() - limit), all.size()));
    }

    @Override
    public synchronized List<ChatMessage> findAfter(long roomId, long afterId, int limit) {
        return rooms.getOrDefault(roomId, List.of()).stream()
                .filter(m -> m.id() > afterId)
                .limit(limit)
                .toList();
    }

    @Override
    public synchronized void deleteRoom(long roomId) {
        rooms.remove(roomId);
        seqs.remove(roomId);
        gameStarts.remove(roomId);
    }

    @Override
    public synchronized void markGameStart(long roomId) {
        AtomicLong seq = seqs.get(roomId);
        gameStarts.put(roomId, seq == null ? 0L : seq.get());
    }

    @Override
    public synchronized long deleteSinceGameStart(long roomId) {
        Long startId = gameStarts.remove(roomId);
        List<ChatMessage> all = rooms.get(roomId);
        if (startId == null || all == null) return 0;
        int before = all.size();
        all.removeIf(m -> m.id() > startId);
        return before - all.size();
    }
}
