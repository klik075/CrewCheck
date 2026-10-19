-- 기본 테이블. MySQL과 H2(MySQL 모드) 양쪽에서 실행되도록 기본 문법만 사용한다.
-- 한 번 적용된 마이그레이션 파일은 수정하지 말고, 변경은 V2, V3... 새 파일로 추가한다.

-- 회원 (member.entity.Member)
CREATE TABLE members (
    id       BIGINT       NOT NULL AUTO_INCREMENT,
    username VARCHAR(50)  NOT NULL,
    password VARCHAR(255) NULL,
    -- OAuth 2.0 소셜 로그인(구글, 카카오) 지원:
--   members.provider / provider_id 추가, 소셜 가입자는 비밀번호가 없으므로 password 를 NULL 허용으로 변경.
    provider VARCHAR(20) NOT NULL DEFAULT 'LOCAL',
    provider_id VARCHAR(100) NULL,
    PRIMARY KEY (id),
    -- 같은 플랫폼의 같은 계정은 한 번만 가입 (provider_id 가 NULL 인 일반 회원은 제외됨)
    CONSTRAINT uk_members_username UNIQUE (username),
    CONSTRAINT uk_members_provider UNIQUE (provider, provider_id)
);

-- 프로필 이미지(아바타 프리셋 번호). 0 = 기본 아바타
-- 로비 유저 (lobby.entity.User)
-- 진영(선원/해적/중립)별 전적. 전체 전적(play_count/win_count)은 그대로 두고 따로 센다.
-- 기존 유저의 진영별 전적은 0에서 시작한다. (이전 게임은 진영을 기록하지 않았다)
CREATE TABLE users (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    member_id   BIGINT      NOT NULL,
    nickname    VARCHAR(20) NOT NULL,
    level       INT         NOT NULL DEFAULT 0,
    gold        INT         NOT NULL DEFAULT 0,
    play_count  INT         NOT NULL DEFAULT 0,
    win_count   INT         NOT NULL DEFAULT 0,
    profile_image_id INT NOT NULL DEFAULT 0,
    crew_play_count    INT NOT NULL DEFAULT 0,
    crew_win_count     INT NOT NULL DEFAULT 0,
    pirate_play_count  INT NOT NULL DEFAULT 0,
    pirate_win_count   INT NOT NULL DEFAULT 0,
    neutral_play_count INT NOT NULL DEFAULT 0,
    neutral_win_count  INT NOT NULL DEFAULT 0,
    PRIMARY KEY (id),
    CONSTRAINT uk_users_member UNIQUE (member_id),
    CONSTRAINT uk_users_nickname UNIQUE (nickname),
    CONSTRAINT fk_users_member FOREIGN KEY (member_id) REFERENCES members (id)
);


-- 직업 정의 (jobs.domain.RoleEntity). 게임 중에는 바뀌지 않는 공개 데이터.
CREATE TABLE roles (
    code        VARCHAR(50) NOT NULL,
    name        VARCHAR(50) NOT NULL,
    faction     VARCHAR(20) NOT NULL,   -- Faction enum: CREW / PIRATE / NEUTRAL
    action_code VARCHAR(50),            -- ActionCode enum 이름, 능력이 없으면 NULL
    enabled     BOOLEAN     NOT NULL,
    PRIMARY KEY (code)
);

-- 제3 세력은 랜덤 구성 후보에서 빠지고 커스텀 구성에서만 고를 수 있다(RoleAssigner).
-- 능력 로직은 단계별로 구현하며, RoleAssigner 구성표(6단계)에 들어가기 전까지는 게임에 배정되지 않는다.
INSERT INTO roles (code, name, faction, action_code, enabled) VALUES
    ('PIRATE_RAIDER',         '해적',      'PIRATE', 'SELECT_ATTACK_TARGET', TRUE),  -- 마피아: 밤에 공격
    ('CREW_CAPTAIN',          '선장',      'CREW',   'INVESTIGATE_FACTION',  TRUE),  -- 경찰: 진영 조사
    ('CREW_DOCTOR',           '선의',      'CREW',   'PROTECT',              TRUE),  -- 의사: 보호
    ('CREW_SAILOR',           '선원',      'CREW',   NULL,                   TRUE),
    ('PIRATE_COOK',           '요리사',    'PIRATE',  'BAN_VOTE',        TRUE),  -- 매일 밤 1명의 다음 투표 금지 (같은 대상 연속 불가)
    ('NEUTRAL_SIREN',         '세이렌',    'NEUTRAL', 'SEDUCE',          TRUE),  -- 유혹해 세이렌 팀으로 (성공한 다음 밤은 쉼)
    ('NEUTRAL_KRAKEN',        '크라켄',    'NEUTRAL', 'KRAKEN_MARK',     TRUE),  -- 표식 또는 발동(KRAKEN_STRIKE)으로 표식된 사람 모두 처치
    ('NEUTRAL_GHOST_CAPTAIN', '유령 선장', 'NEUTRAL', NULL,              TRUE),  -- 능력 없음. 밤에 죽지 않음
    ('NEUTRAL_MERMAID',       '인어',     'NEUTRAL', NULL,              TRUE),-- 시민: 능력 없음
    ('CREW_LOOKOUT',          '망루지기', 'CREW',   'WATCH_VISITORS',   TRUE),
    ('CREW_BOATSWAIN',        '갑판장',   'CREW',   'BLOCK',            TRUE),
    ('CREW_MONKEY',           '원숭이',   'CREW',   NULL,               TRUE),
    ('CREW_DRUNK',            '주정뱅이', 'CREW',   'READ_CORPSE_ROLE', TRUE),
    ('PIRATE_PARROT',         '앵무새',   'PIRATE', 'WATCH_ACTION',     TRUE);

CREATE TABLE friendships (
                             id           BIGINT      NOT NULL AUTO_INCREMENT,
                             requester_id BIGINT      NOT NULL,
                             receiver_id  BIGINT      NOT NULL,
                             status       VARCHAR(20) NOT NULL,
                             created_at   DATETIME(6) NOT NULL,
                             PRIMARY KEY (id),
                             CONSTRAINT uk_friendship_requester_receiver UNIQUE (requester_id, receiver_id),
                             CONSTRAINT fk_friendships_requester FOREIGN KEY (requester_id) REFERENCES users (id) ON DELETE CASCADE,
                             CONSTRAINT fk_friendships_receiver  FOREIGN KEY (receiver_id)  REFERENCES users (id) ON DELETE CASCADE
);

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
