package com.WhoisntCitizen_server.support;

import com.WhoisntCitizen_server.game.repository.GameRepository;
import com.WhoisntCitizen_server.game.repository.GameRepositoryContractTest;

class CopyingGameRepositoryContractTest extends GameRepositoryContractTest {

    @Override
    protected GameRepository newRepository() {
        return new CopyingGameRepository();
    }
}
