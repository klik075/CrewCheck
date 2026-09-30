package com.WhoisntCitizen_server.jobs.domain;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.Table;

/** 조사 결과와 공개 사건을 수신자별로 저장한다. 다른 참가자의 비공개 보고는 조회할 수 없다. */
@Entity
@Table(name = "room_reports")
public class RoomReportEntity {
    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Column(name = "room_id", nullable = false)
    private Long roomId;

    @Column(name = "recipient_player_id", nullable = false)
    private Long recipientPlayerId;

    @Column(name = "round_number", nullable = false)
    private int roundNumber;

    @Column(nullable = false)
    private String type;

    @Column(nullable = false, length = 500)
    private String message;

    protected RoomReportEntity() {}

    public RoomReportEntity(Long roomId, Long recipientPlayerId, int roundNumber,
                            String type, String message) {
        this.roomId = roomId;
        this.recipientPlayerId = recipientPlayerId;
        this.roundNumber = roundNumber;
        this.type = type;
        this.message = message;
    }

    public Long getId() { return id; }
    public int getRoundNumber() { return roundNumber; }
    public String getType() { return type; }
    public String getMessage() { return message; }
}
