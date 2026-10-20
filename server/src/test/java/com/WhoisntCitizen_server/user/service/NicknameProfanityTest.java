package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.chat.filter.ProfanityFilter;
import com.WhoisntCitizen_server.common.exception.NicknameNotAllowedException;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.util.Optional;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;
import static org.mockito.ArgumentMatchers.any;
import static org.mockito.Mockito.mock;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;

/** 닉네임 변경: 욕설·금칙어가 든 닉네임은 거절하고, 정상 닉네임은 바뀐다. */
class NicknameProfanityTest {

    private UserRepository userRepository;
    private UserService userService;
    private User user;

    @BeforeEach
    void setUp() {
        userRepository = mock(UserRepository.class);
        ProfanityFilter filter = ProfanityFilter.of(java.util.List.of("시발"), java.util.List.of());
        userService = new UserService(userRepository, filter);
        user = User.builder().memberId(1L).nickname("기존닉").build();
        when(userRepository.findByMemberId(1L)).thenReturn(Optional.of(user));
    }

    @Test
    void 금칙어가_든_닉네임으로는_바꿀_수_없다() {
        assertThatThrownBy(() -> userService.updateNickname(1L, "시발"))
                .isInstanceOf(NicknameNotAllowedException.class);
        assertThatThrownBy(() -> userService.updateNickname(1L, "시.발이"))
                .isInstanceOf(NicknameNotAllowedException.class);

        assertThat(user.getNickname()).isEqualTo("기존닉");
        verify(userRepository, never()).existsByNickname(any());
    }

    @Test
    void 정상_닉네임은_바뀐다() {
        when(userRepository.existsByNickname("새닉네임")).thenReturn(false);

        userService.updateNickname(1L, "새닉네임");

        assertThat(user.getNickname()).isEqualTo("새닉네임");
    }

    @Test
    void 에러_코드는_NICKNAME_NOT_ALLOWED이다() {
        assertThat(NicknameNotAllowedException.CODE).isEqualTo("NICKNAME_NOT_ALLOWED");
    }
}
