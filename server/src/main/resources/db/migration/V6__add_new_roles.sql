-- 신규 직업 5종. 요리사는 해적 진영, 나머지는 제3 세력(NEUTRAL)이다.
-- 제3 세력은 랜덤 구성 후보에서 빠지고 커스텀 구성에서만 고를 수 있다(RoleAssigner).
INSERT INTO roles (code, name, faction, action_code, enabled) VALUES
    ('PIRATE_COOK',           '요리사',    'PIRATE',  'BAN_VOTE',    TRUE),  -- 매일 밤 1명의 다음 투표 금지 (같은 대상 연속 불가)
    ('NEUTRAL_SIREN',         '세이렌',    'NEUTRAL', 'SEDUCE',      TRUE),  -- 유혹해 세이렌 팀으로 (성공한 다음 밤은 쉼)
    ('NEUTRAL_KRAKEN',        '크라켄',    'NEUTRAL', 'KRAKEN_MARK', TRUE),  -- 표식 또는 발동(KRAKEN_STRIKE)으로 표식된 사람 모두 처치
    ('NEUTRAL_GHOST_CAPTAIN', '유령 선장', 'NEUTRAL', NULL,          TRUE),  -- 능력 없음. 밤에 죽지 않음
    ('NEUTRAL_MERMAID',       '인어',      'NEUTRAL', NULL,          TRUE);  -- 능력 없음. 처형되면 승리
