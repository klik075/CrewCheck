package com.WhoisntCitizen_server.common.validation;

import jakarta.validation.Constraint;
import jakarta.validation.ConstraintValidator;
import jakarta.validation.ConstraintValidatorContext;
import jakarta.validation.Payload;

import java.lang.annotation.*;
import java.util.regex.Pattern;

@Documented
@Constraint(validatedBy = ValidNickname.Validator.class)
@Target({ElementType.FIELD, ElementType.PARAMETER})
@Retention(RetentionPolicy.RUNTIME)
public @interface ValidNickname {

	String message() default "닉네임은 한글 최대 6자, 영문/숫자 최대 9자(한글, 영문, 숫자 조합 2자 이상)여야 합니다.";
	Class<?>[] groups() default {};
	Class<? extends Payload>[] payload() default {};

	class Validator implements ConstraintValidator<ValidNickname, String> {
		private static final Pattern NICKNAME_PATTERN = Pattern.compile("^[a-zA-Z0-9가-힣]+$");

		@Override
		public boolean isValid(String nickname, ConstraintValidatorContext context) {
			if (nickname == null || nickname.isBlank()) {
				return false;
			}

			// 허용 문자: 한글, 영문, 숫자
			if (!NICKNAME_PATTERN.matcher(nickname).matches()) {
				return false;
			}

			// 최소 2자 검증
			if (nickname.length() < 2) {
				return false;
			}

			// 가중치 계산 (한글: 3점, 영문/숫자: 2점 -> 최대 18점)
			int score = 0;
			for (char c : nickname.toCharArray()) {
				if (c >= '가' && c <= '힣') {
					score += 3; // 한글 6자 * 3 = 18점
				} else {
					score += 2; // 영문/숫자 9자 * 2 = 18점
				}
			}

			return score <= 18;
		}
	}
}