package com.sparta.unityaitestproject_server.controller;

import com.sparta.unityaitestproject_server.dto.EchoRequest;
import com.sparta.unityaitestproject_server.dto.HelloResponse;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api")
public class TestController {

    private static final Logger log = LoggerFactory.getLogger(TestController.class);

    // GET http://localhost:8080/api/hello
    @GetMapping("/hello")
    public HelloResponse hello() {
        log.info("[GET /api/hello] 요청 받음");
        return new HelloResponse("Hello from Spring!", System.currentTimeMillis());
    }

    // POST http://localhost:8080/api/echo
    // Body: {"name":"원우","score":100}
    @PostMapping("/echo")
    public EchoRequest echo(@RequestBody EchoRequest req) {
        log.info("[POST /api/echo] 받은 값: name={}, score={}", req.name(), req.score());
        return req; // 받은 값을 그대로 돌려줌
    }
}
