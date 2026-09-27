package com.sparta.unityaitestproject_server.dto;

// GET /api/hello 응답
// Unity의 HelloResponse(message, timestamp)와 필드 이름이 같아야 합니다.
public record HelloResponse(String message, long timestamp) {}
