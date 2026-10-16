-- 진영(선원/해적/중립)별 전적. 전체 전적(play_count/win_count)은 그대로 두고 따로 센다.
-- 기존 유저의 진영별 전적은 0에서 시작한다. (이전 게임은 진영을 기록하지 않았다)
ALTER TABLE users
    ADD COLUMN crew_play_count    INT NOT NULL DEFAULT 0,
    ADD COLUMN crew_win_count     INT NOT NULL DEFAULT 0,
    ADD COLUMN pirate_play_count  INT NOT NULL DEFAULT 0,
    ADD COLUMN pirate_win_count   INT NOT NULL DEFAULT 0,
    ADD COLUMN neutral_play_count INT NOT NULL DEFAULT 0,
    ADD COLUMN neutral_win_count  INT NOT NULL DEFAULT 0;
