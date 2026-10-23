using System;
using NUnit.Framework;

namespace WhoisntCitizen.Game.Tests
{
    /// <summary>GameApiPaths(서버 컨트롤러 경로)와 GameApiResult.</summary>
    public class GameApiTests
    {
        // 서버 dev b41b546: GameController(/api/v1/games), NightController·VoteController(/api/v1/games/{gameId})
        [Test]
        public void 경로는_서버_컨트롤러와_같다()
        {
            const string id = "3f2b9c1e-7d4a-4b8e-9a1f-2c6d8e0b5a71";

            Assert.AreEqual("/api/v1/games/" + id, GameApiPaths.State(id));
            Assert.AreEqual("/api/v1/games/" + id + "/me", GameApiPaths.Me(id));
            Assert.AreEqual("/api/v1/games/" + id + "/night-actions", GameApiPaths.NightActions(id));
            Assert.AreEqual("/api/v1/games/" + id + "/night-actions/skip", GameApiPaths.SkipNightAction(id));
            Assert.AreEqual("/api/v1/games/" + id + "/night-result", GameApiPaths.NightResult(id));
            Assert.AreEqual("/api/v1/games/" + id + "/votes", GameApiPaths.Votes(id));
            Assert.AreEqual("/api/v1/games/" + id + "/execution-result", GameApiPaths.ExecutionResult(id));
            Assert.AreEqual("/api/v1/games/" + id + "/result", GameApiPaths.Result(id));
        }

        [Test]
        public void gameId는_URL_인코딩한다()
        {
            Assert.AreEqual("/api/v1/games/a%20b%2Fc", GameApiPaths.State("a b/c"));
            Assert.AreEqual("/api/v1/games/", GameApiPaths.State(null));
        }

        [Test]
        public void 성공_결과()
        {
            var state = new GameStateDto();
            var r = GameApiResult<GameStateDto>.Ok(state);

            Assert.IsTrue(r.Success);
            Assert.AreEqual(200L, r.StatusCode);
            Assert.AreSame(state, r.Data);
            Assert.IsNull(r.ErrorCode);
            Assert.IsFalse(r.IsConnectionError);
        }

        [Test]
        public void 성공인데_데이터가_없으면_예외()
        {
            Assert.Throws<ArgumentNullException>(() => GameApiResult<GameStateDto>.Ok(null));
        }

        [Test]
        public void 실패_결과_종류()
        {
            var rule = GameApiResult<GameStateDto>.Fail(409, GameErrorCodes.GameRuleViolation, "규칙 위반");
            var conflict = GameApiResult<GameStateDto>.Fail(409, GameErrorCodes.Conflict, "충돌");
            var offline = GameApiResult<GameStateDto>.Fail(0, null, "서버에 연결할 수 없습니다.");
            var gone = GameApiResult<GameStateDto>.Fail(404, GameErrorCodes.GameNotFound, "없음");

            Assert.IsFalse(rule.Success);
            Assert.IsNull(rule.Data);
            Assert.IsTrue(rule.IsRuleViolation);
            Assert.AreEqual("규칙 위반", rule.Message);
            Assert.IsFalse(conflict.IsRuleViolation);
            Assert.IsTrue(offline.IsConnectionError);
            Assert.IsFalse(rule.IsConnectionError);
            Assert.IsTrue(gone.IsNotFound);
        }
    }
}
