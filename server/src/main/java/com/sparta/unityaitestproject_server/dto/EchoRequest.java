package com.sparta.unityaitestproject_server.dto;

// POST /api/echo 요청/응답
// Unity의 EchoRequest(name, score)와 필드 이름이 같아야 합니다.
public record EchoRequest(String name, int score) {}
