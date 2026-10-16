using System.Text;

namespace WhoisntCitizen.Profile
{
    /// <summary>
    /// 진영(선원/해적/중립)별 전적 표시 도우미. 서버가 주는 판수·승수 6개로 글자를 만든다.
    /// 내 프로필 / 친구 프로필 / 상단 패널이 같은 형식을 쓰도록 한곳에 모았다.
    /// </summary>
    public static class FactionRecord
    {
        /// <summary>"13승 7패" (상단 패널용)</summary>
        public static string WinLoss(int playCount, int winCount)
        {
            return $"{winCount}승 {System.Math.Max(0, playCount - winCount)}패";
        }

        /// <summary>"20전 13승 65%" — 판수가 0이면 승률 대신 "-" (0%로 보이면 오해하기 쉽다)</summary>
        public static string Line(int playCount, int winCount)
        {
            string rate = playCount <= 0 ? "-" : $"{(double)winCount / playCount * 100d:0.#}%";
            return $"{playCount}전 {winCount}승 {rate}";
        }

        /// <summary>프로필 창용 세 줄: 선원 / 해적 / 중립 (왼쪽 제목 칸)</summary>
        public static string Titles()
        {
            return "선원\n해적\n중립";
        }

        /// <summary>프로필 창용 세 줄: 진영별 전적 (오른쪽 값 칸)</summary>
        public static string Lines(int crewPlay, int crewWin, int piratePlay, int pirateWin, int neutralPlay, int neutralWin)
        {
            return new StringBuilder()
                .Append(Line(crewPlay, crewWin)).Append('\n')
                .Append(Line(piratePlay, pirateWin)).Append('\n')
                .Append(Line(neutralPlay, neutralWin))
                .ToString();
        }

        public static string Lines(UserProfileResponse p)
        {
            return Lines(p.crewPlayCount, p.crewWinCount, p.piratePlayCount, p.pirateWinCount, p.neutralPlayCount, p.neutralWinCount);
        }
    }
}
