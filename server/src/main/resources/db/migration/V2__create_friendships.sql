CREATE TABLE friendships (
                             id           BIGINT      NOT NULL AUTO_INCREMENT,
                             requester_id BIGINT      NOT NULL,
                             receiver_id  BIGINT      NOT NULL,
                             status       VARCHAR(20) NOT NULL,
                             created_at   DATETIME(6) NOT NULL,
                             PRIMARY KEY (id),
                             CONSTRAINT uk_friendship_requester_receiver UNIQUE (requester_id, receiver_id),
                             CONSTRAINT fk_friendships_requester FOREIGN KEY (requester_id) REFERENCES users (id) ON DELETE CASCADE,
                             CONSTRAINT fk_friendships_receiver  FOREIGN KEY (receiver_id)  REFERENCES users (id) ON DELETE CASCADE
);
