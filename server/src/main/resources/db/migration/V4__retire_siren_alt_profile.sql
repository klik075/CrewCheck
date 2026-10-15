-- 삭제된 CONCEPT_SIREN_ALT(20)를 기존 CONCEPT_SIREN(19)으로 이전한다.
UPDATE users SET profile_image_id = 19 WHERE profile_image_id = 20;
