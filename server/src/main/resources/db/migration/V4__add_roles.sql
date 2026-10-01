-- 신규 직업 5종(해적 테마). V2의 기본 4종에 이어서 등록한다.
-- 능력 로직은 단계별로 구현하며, RoleAssigner 구성표(6단계)에 들어가기 전까지는 게임에 배정되지 않는다.
INSERT INTO roles (code, name, faction, action_code, enabled) VALUES
    ('CREW_LOOKOUT',   '망루지기', 'CREW',   'WATCH_VISITORS',   TRUE),
    ('CREW_BOATSWAIN', '갑판장',   'CREW',   'BLOCK',            TRUE),
    ('CREW_MONKEY',    '원숭이',   'CREW',   NULL,               TRUE),
    ('CREW_DRUNK',     '주정뱅이', 'CREW',   'READ_CORPSE_ROLE', TRUE),
    ('PIRATE_PARROT',  '앵무새',   'PIRATE', 'WATCH_ACTION',     TRUE);