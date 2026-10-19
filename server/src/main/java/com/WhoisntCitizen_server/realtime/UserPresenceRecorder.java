package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.repository.LobbyRoomRepository;
import com.WhoisntCitizen_server.user.service.CurrentUserResolver;
import com.WhoisntCitizen_server.user.service.UserPresenceService;
import com.WhoisntCitizen_server.user.service.UserPresenceStatus;

import java.time.Duration;
import java.util.Objects;
import java.util.OptionalLong;

/**
 * 연결 상태 → UserPresenceService(Redis). memberId를 userId(User.id)로 바꿔서 기록한다. (memberId→userId 캐시는 CurrentUserResolver)
 *
 * 상태는 "지금 이 연결이 구독 중인 방"으로 매번 새로 계산한다. 그래서 방이 삭제되거나 강퇴로 구독이 풀리거나 게임이 시작돼도,
 * 다음 ping(10초 이내)에서 저절로 맞춰진다. 방은 잠금 없이 읽는다 (GameActivityRecorder와 같다).
 */
public class UserPresenceRecorder implements RealtimePresenceRecorder {

    private final UserPresenceService presenceService;
    private final CurrentUserResolver userResolver;
    private final LobbyRoomRepository roomRepository;

    public UserPresenceRecorder(UserPresenceService presenceService, CurrentUserResolver userResolver,
                                LobbyRoomRepository roomRepository) {
        this.presenceService = Objects.requireNonNull(presenceService, "presenceService");
        this.userResolver = Objects.requireNonNull(userResolver, "userResolver");
        this.roomRepository = Objects.requireNonNull(roomRepository, "roomRepository");
    }

    @Override
    public void update(long memberId, OptionalLong roomId) {
        Long userId = userResolver.userIdByMemberId(memberId);
        presenceService.setStatus(userId, statusOf(roomId));
    }

    @Override
    public void offline(long memberId) {
        Long userId = userResolver.userIdByMemberId(memberId);
        presenceService.removePresence(userId);
    }

    @Override
    public void expireSoon(long memberId, Duration grace) {
        Long userId = userResolver.userIdByMemberId(memberId);
        presenceService.expireSoon(userId, grace);
    }

    private UserPresenceStatus statusOf(OptionalLong roomId) {
        if (roomId.isEmpty()) {
            return UserPresenceStatus.ONLINE;
        }
        Room room = roomRepository.findById(roomId.getAsLong());
        if (room == null) {
            return UserPresenceStatus.ONLINE; // 구독 중이던 방이 사라졌다
        }
        return room.isInGame() ? UserPresenceStatus.IN_GAME : UserPresenceStatus.IN_ROOM;
    }
}
