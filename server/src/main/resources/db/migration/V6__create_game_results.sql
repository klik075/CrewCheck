-- 끝난 게임의 결과 (game.result.GameResult). 회원 전적을 게임마다 "정확히 한 번" 반영하기 위한 기록이다.
--  - 게임이 끝나는 순간(게임 잠금 안, 게임 상태 저장 전) 서버가 바로 넣는다. 서버 메모리의 이벤트가 사라져도 이 행은 남는다.
--  - game_id가 기본 키라 같은 게임은 한 번만 들어간다.
--  - stats_applied_at이 NULL이면 아직 회원 전적에 반영하지 않은 게임이다(outbox). 전적을 올리는 트랜잭션에서 함께 채운다.
--  - 전적에 들어가는 게임(로비 방에서 시작해 승패가 난 게임)만 남긴다. 개발용 게임과 취소된 게임은 넣지 않는다.
CREATE TABLE game_results (
    game_id          VARCHAR(64) NOT NULL,   -- Game.gameId (UUID)
    room_id          VARCHAR(64) NOT NULL,
    winner           VARCHAR(20) NOT NULL,   -- Winner enum
    end_reason       VARCHAR(30) NOT NULL,   -- GameEndReason enum (지금은 WIN만 들어온다)
    last_day         INT         NOT NULL,
    ended_at         DATETIME(6) NOT NULL,
    stats_applied_at DATETIME(6) NULL,
    PRIMARY KEY (game_id)
);

-- 반영 안 된 결과 찾기 (PendingGameStatsRelay)
CREATE INDEX idx_game_results_pending ON game_results (stats_applied_at, ended_at);

-- 게임 참가자별 결과. user_id는 users.id지만, 탈퇴한 회원이 있어도 결과가 남도록 외래 키를 걸지 않는다.
CREATE TABLE game_result_players (
    game_id   VARCHAR(64) NOT NULL,
    user_id   BIGINT      NOT NULL,
    role_code VARCHAR(50) NOT NULL,
    faction   VARCHAR(20) NULL,           -- Faction enum: CREW / PIRATE / NEUTRAL
    alive     BOOLEAN     NOT NULL,
    win       BOOLEAN     NOT NULL,
    PRIMARY KEY (game_id, user_id),
    CONSTRAINT fk_game_result_players_game FOREIGN KEY (game_id) REFERENCES game_results (game_id) ON DELETE CASCADE
);
