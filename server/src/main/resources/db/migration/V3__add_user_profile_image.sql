-- 프로필 이미지(아바타 프리셋 번호). 0 = 기본 아바타
ALTER TABLE users ADD COLUMN profile_image_id INT NOT NULL DEFAULT 0;
