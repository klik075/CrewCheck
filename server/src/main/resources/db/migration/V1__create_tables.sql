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

-- 로비 유저 (lobby.entity.User)
CREATE TABLE users (
    id          BIGINT      NOT NULL AUTO_INCREMENT,
    member_id   BIGINT      NOT NULL,
    nickname    VARCHAR(20) NOT NULL,
    level       INT         NOT NULL DEFAULT 0,
    gold        INT         NOT NULL DEFAULT 0,
    play_count  INT         NOT NULL DEFAULT 0,
    win_count   INT         NOT NULL DEFAULT 0,
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

