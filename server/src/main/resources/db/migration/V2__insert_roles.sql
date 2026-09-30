-- 기본 직업 4종 (해적 테마).
-- 게임 로직의 마피아/경찰/의사/시민에 대응한다.
-- 직업을 추가할 때는 이 파일을 고치지 말고 V3__add_roles.sql 같은 새 파일로 INSERT 한다.
INSERT INTO roles (code, name, faction, action_code, enabled) VALUES
    ('PIRATE_RAIDER', '해적', 'PIRATE', 'SELECT_ATTACK_TARGET', TRUE),  -- 마피아: 밤에 공격
    ('CREW_CAPTAIN',  '선장', 'CREW',   'INVESTIGATE_FACTION',  TRUE),  -- 경찰: 진영 조사
    ('CREW_DOCTOR',   '선의', 'CREW',   'PROTECT',              TRUE),  -- 의사: 보호
    ('CREW_SAILOR',   '선원', 'CREW',   NULL,                   TRUE);  -- 시민: 능력 없음
