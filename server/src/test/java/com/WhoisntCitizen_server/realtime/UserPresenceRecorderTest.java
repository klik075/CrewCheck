package com.WhoisntCitizen_server.realtime;

import com.WhoisntCitizen_server.chat.InMemoryLobbyRoomRepository;
import com.WhoisntCitizen_server.lobby.domain.room.Room;
import com.WhoisntCitizen_server.lobby.domain.room.RoomPlayer;
import com.WhoisntCitizen_server.user.service.CurrentUserResolver;
import com.WhoisntCitizen_server.user.service.UserPresenceService;
import com.WhoisntCitizen_server.user.service.UserPresenceStatus;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.time.Duration;
import java.util.OptionalLong;

import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

/** 연결 상태(구독 중인 방)가 어떤 접속 상태로 기록되는지, memberId가 userId로 바뀌어 기록되는지 확인한다. */
class UserPresenceRecorderTest {

    private static final long MEMBER_ID = 7L; // 토큰 sub
    private static final long USER_ID = 15L;  // User.id (Redis 키에 쓰는 값)

    private final InMemoryLobbyRoomRepository rooms = new InMemoryLobbyRoomRepository();
    private final UserPresenceService presenceService = mock(UserPresenceService.class);
    private final CurrentUserResolver resolver = mock(CurrentUserResolver.class);
    private final UserPresenceRecorder recorder = new UserPresenceRecorder(presenceService, resolver, rooms);
    private Room room;

    @BeforeEach
    void setUp() {
        when(resolver.userIdByMemberId(MEMBER_ID)).thenReturn(USER_ID);
        room = new Room(3L, "방", USER_ID, 6);
        room.addPlayer(new RoomPlayer(USER_ID, "철수", false));
        rooms.save(room);
    }

    @Test
    void 방을_구독하지_않으면_ONLINE으로_기록한다() {
        recorder.update(MEMBER_ID, OptionalLong.empty());

        verify(presenceService).setStatus(USER_ID, UserPresenceStatus.ONLINE);
    }

    @Test
    void 대기_중인_방을_구독하면_IN_ROOM으로_기록한다() {
        recorder.update(MEMBER_ID, OptionalLong.of(3L));

        verify(presenceService).setStatus(USER_ID, UserPresenceStatus.IN_ROOM);
    }

    @Test
    void 게임이_진행_중인_방을_구독하면_IN_GAME으로_기록한다() {
        room.startGame("g-1");

        recorder.update(MEMBER_ID, OptionalLong.of(3L));

        verify(presenceService).setStatus(USER_ID, UserPresenceStatus.IN_GAME);
    }

    @Test
    void 구독한_방이_사라졌으면_ONLINE으로_기록한다() {
        recorder.update(MEMBER_ID, OptionalLong.of(999L));

        verify(presenceService).setStatus(USER_ID, UserPresenceStatus.ONLINE);
    }

    @Test
    void 오프라인으로_바꿀_때도_userId로_지운다() {
        recorder.offline(MEMBER_ID);

        verify(presenceService).removePresence(USER_ID);
    }

    @Test
    void 서버_종료로_끊기면_지우지_않고_유효시간만_줄인다() {
        recorder.expireSoon(MEMBER_ID, Duration.ofSeconds(10));

        verify(presenceService).expireSoon(USER_ID, Duration.ofSeconds(10));
        verify(presenceService, never()).removePresence(USER_ID);
    }
}
