package com.WhoisntCitizen_server.common.exception;

/**
 * 닉네임에 욕설·금칙어가 들어 있음 → 400 NICKNAME_NOT_ALLOWED.
 *
 * 형식 오류(BAD_REQUEST)·중복(CONFLICT)과 code를 구분해 Unity가 경고 문구를 따로 띄울 수 있게 한다.
 */
public class NicknameNotAllowedException extends RuntimeException {

    public static final String CODE = "NICKNAME_NOT_ALLOWED";

    public NicknameNotAllowedException() {
        super("사용할 수 없는 닉네임입니다. 욕설이나 부적절한 단어가 포함되어 있습니다.");
    }
}
