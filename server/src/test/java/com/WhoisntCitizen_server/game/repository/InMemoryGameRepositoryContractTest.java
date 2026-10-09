package com.WhoisntCitizen_server.game.repository;

class InMemoryGameRepositoryContractTest extends GameRepositoryContractTest {

    @Override
    protected GameRepository newRepository() {
        return new InMemoryGameRepository();
    }
}
