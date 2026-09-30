package com.WhoisntCitizen_server.lobby.repository;

import com.WhoisntCitizen_server.lobby.entity.User;
import org.springframework.data.jpa.repository.JpaRepository;

public interface UserRepository extends JpaRepository<User,Long> {
}

