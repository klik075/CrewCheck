package com.WhoisntCitizen_server.user.service;

import com.WhoisntCitizen_server.game.result.GameResult;
import com.WhoisntCitizen_server.game.result.GameResultPlayer;
import com.WhoisntCitizen_server.game.result.GameResultService;
import com.WhoisntCitizen_server.user.entity.User;
import com.WhoisntCitizen_server.user.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import lombok.extern.slf4j.Slf4j;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.OptionalInt;
import java.util.function.Function;
import java.util.stream.Collectors;

/** 9. 게임 결과(game_results)를 회원 전적(User.playCount / winCount)에 반영한다. */
@Slf4j
@Service
@RequiredArgsConstructor
public class UserStatsService {

    private final UserRepository userRepository;
    private final GameResultService gameResultService;

    /**
     * 한 게임의 참가자 전적을 한 트랜잭션으로 저장한다. 같은 게임은 몇 번 불러도 한 번만 반영된다.
     *  - 먼저 그 게임의 반영 시각을 적어 반영할 차례를 잡는다(GameResultService.claimForStats). 이미 반영됐으면 빈 값
     *  - 잡았으면 같은 트랜잭션에서 전적을 올린다. 도중에 실패하면 반영 시각도 함께 롤백돼 나중에 다시 시도된다
     * 트랜잭션이 끝나면 변경 감지(dirty checking)로 UPDATE가 자동 실행된다. 없는 userId(탈퇴 등)는 건너뛴다.
     * @return 이번에 반영했으면 전적을 올린 회원 수, 이미 반영됐거나 결과가 기록되지 않은 게임이면 빈 값
     */
    @Transactional
    public OptionalInt applyGameResult(String gameId) {
        Optional<GameResult> claimed = gameResultService.claimForStats(gameId);
        if (claimed.isEmpty()) {
            return OptionalInt.empty();
        }
        List<GameResultPlayer> players = claimed.get().getPlayers();
        List<Long> userIds = players.stream()
                .map(GameResultPlayer::getUserId)
                .toList();
        Map<Long, User> users = userRepository.findAllById(userIds).stream()
                .collect(Collectors.toMap(User::getId, Function.identity()));

        int updated = 0;
        for (GameResultPlayer player : players) {
            User user = users.get(player.getUserId());
            if (user == null) {
                log.warn("[{}] 전적 저장 건너뜀: 존재하지 않는 userId={}", gameId, player.getUserId());
                continue;
            }
            user.recordGame(player.getFaction(), player.isWin());
            updated++;
        }
        return OptionalInt.of(updated);
    }
}
