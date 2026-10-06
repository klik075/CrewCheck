-- OAuth 2.0 소셜 로그인(구글, 카카오) 지원:
--   members.provider / provider_id 추가, 소셜 가입자는 비밀번호가 없으므로 password 를 NULL 허용으로 변경.
ALTER TABLE members ADD COLUMN provider VARCHAR(20) NOT NULL DEFAULT 'LOCAL';
ALTER TABLE members ADD COLUMN provider_id VARCHAR(100) NULL;
ALTER TABLE members MODIFY COLUMN password VARCHAR(255) NULL;
-- 같은 플랫폼의 같은 계정은 한 번만 가입 (provider_id 가 NULL 인 일반 회원은 제외됨)
ALTER TABLE members ADD CONSTRAINT uk_members_provider UNIQUE (provider, provider_id);
