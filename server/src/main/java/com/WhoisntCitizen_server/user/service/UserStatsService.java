package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.game.event.GameEndedEvent;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Map;
import java.util.function.Function;
import java.util.stream.Collectors;

/** 9. 게임 결과를 회원 전적(User.playCount / winCount)에 반영한다. */
@Slf4j
@Service
@RequiredArgsConstructor
public class UserStatsService {

    private final UserRepository userRepository;

    /**
     * 한 게임의 참가자 전적을 한 트랜잭션으로 저장한다.
     * 트랜잭션이 끝나면 변경 감지(dirty checking)로 UPDATE가 자동 실행된다.
     * 없는 userId(탈퇴 등)는 건너뛴다.
     */
    @Transactional
    public int recordGameResult(GameEndedEvent event) {
        List<Long> userIds = event.outcomes().stream()
                .map(GameEndedEvent.PlayerOutcome::userId)
                .toList();
        Map<Long, User> users = userRepository.findAllById(userIds).stream()
                .collect(Collectors.toMap(User::getId, Function.identity()));

        int updated = 0;
        for (GameEndedEvent.PlayerOutcome outcome : event.outcomes()) {
            User user = users.get(outcome.userId());
            if (user == null) {
                log.warn("[{}] 전적 저장 건너뜀: 존재하지 않는 userId={}", event.gameId(), outcome.userId());
                continue;
            }
            user.recordGame(outcome.win());
            updated++;
        }
        return updated;
    }
}
