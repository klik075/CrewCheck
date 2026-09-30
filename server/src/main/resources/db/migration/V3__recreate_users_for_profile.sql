-- dev 브랜치 병합으로 회원 구조가 바뀜:
--   Member(members) = 로그인 정보, User(users) = 프로필(닉네임, 레벨, 골드, 전적). Player 엔티티는 삭제됨.
-- V1의 players / users 테이블을 새 User 엔티티(member.entity.User)에 맞게 다시 만든다.
-- 개발 단계라 기존 users 데이터는 버린다.
DROP TABLE players;
DROP TABLE users;

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
