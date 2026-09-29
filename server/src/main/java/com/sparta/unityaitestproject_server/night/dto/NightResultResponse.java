package com.sparta.unityaitestproject_server.night.dto;

/**
 * 4. 밤 결과 공개.
 * investigation은 요청자가 경찰이고 그날 밤 조사했을 때만 채워진다.
 */
public record NightResultResponse(
        int day,
        Long killedPlayerId,
        String killedNickname,
        boolean protectedByDoctor,
        Investigation investigation
) {
    public record Investigation(Long targetId, String targetNickname, boolean mafia) {
    }
}
