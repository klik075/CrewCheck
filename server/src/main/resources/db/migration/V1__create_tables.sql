-- 기본 테이블. MySQL과 H2(MySQL 모드) 양쪽에서 실행되도록 기본 문법만 사용한다.
-- 한 번 적용된 마이그레이션 파일은 수정하지 말고, 변경은 V3, V4... 새 파일로 추가한다.

-- 회원 (member.entity.Member)
CREATE TABLE members (
    id       BIGINT       NOT NULL AUTO_INCREMENT,
    username VARCHAR(50)  NOT NULL,
    password VARCHAR(255) NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT uk_members_username UNIQUE (username)
);

-- 플레이어 프로필 (member.entity.Player)
CREATE TABLE players (
    id        BIGINT       NOT NULL AUTO_INCREMENT,
    nickname  VARCHAR(255) NOT NULL,
    level     INT          NOT NULL DEFAULT 0,
    gold      INT          NOT NULL DEFAULT 0,
    member_id BIGINT       NOT NULL,
    PRIMARY KEY (id),
    CONSTRAINT uk_players_nickname UNIQUE (nickname),
    CONSTRAINT fk_players_member FOREIGN KEY (member_id) REFERENCES members (id)
);

-- 로비 유저 (lobby.entity.User)
CREATE TABLE users (
    id       BIGINT      NOT NULL AUTO_INCREMENT,
    nickname VARCHAR(50) NOT NULL,
    PRIMARY KEY (id)
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
