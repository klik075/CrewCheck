using System;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// WebSocket이 끊겼을 때 "몇 초 뒤에 다시 연결할지"를 정한다. (지수 백오프)
    ///
    ///   실패 1회 → 1초, 2회 → 2초, 3회 → 4초, 4회 → 8초, 5회 → 16초, 6회부터 → 30초(상한)
    ///
    /// 연결에 성공해 서버의 connected를 받으면 Reset()으로 처음(1초)부터 다시 센다.
    /// 끊긴 동안에도 화면은 1~2초 폴링으로 계속 진행되므로, 재연결은 서두르지 않고 서버 부담을 줄이는 쪽을 택한다.
    ///
    /// 지터(jitter): 서버가 재시작되면 모든 클라이언트가 같은 순간에 끊기고 같은 간격으로 다시 붙으려 한다.
    /// 대기시간에 ±jitterRatio(기본 20%)만큼 무작위 값을 섞어 접속 시도가 한꺼번에 몰리지 않게 흩는다.
    ///
    /// 시간을 직접 재지 않고 "기다릴 초"만 돌려준다. 실제로 기다리는 일은 RealtimeClient가 한다. (그래서 테스트가 즉시 끝난다)
    /// </summary>
    public sealed class ReconnectBackoff
    {
        public const double DefaultInitialSeconds = 1.0;
        public const double DefaultMaxSeconds = 30.0;
        public const double DefaultJitterRatio = 0.2;

        // 2^30이면 어떤 상한이든 이미 넘는다. 시도 횟수가 아주 커져도 계산이 넘치지 않게 지수를 여기서 멈춘다.
        private const int MaxExponent = 30;

        private readonly double initialSeconds;
        private readonly double maxSeconds;
        private readonly double jitterRatio;
        private readonly Func<double> random;

        /// <summary>지금까지 연달아 실패한 횟수. Reset()하면 0.</summary>
        public int Attempt { get; private set; }

        /// <param name="initialSeconds">첫 대기시간(초). 0보다 커야 한다.</param>
        /// <param name="maxSeconds">대기시간 상한(초). initialSeconds 이상.</param>
        /// <param name="jitterRatio">무작위로 흔들 비율. 0 이상 1 미만. 0이면 흔들지 않는다.</param>
        /// <param name="random">0 이상 1 미만의 값을 주는 함수. 테스트에서 고정값을 넣는다. null이면 System.Random.</param>
        public ReconnectBackoff(
            double initialSeconds = DefaultInitialSeconds,
            double maxSeconds = DefaultMaxSeconds,
            double jitterRatio = DefaultJitterRatio,
            Func<double> random = null)
        {
            if (!(initialSeconds > 0))
            {
                throw new ArgumentOutOfRangeException(nameof(initialSeconds), initialSeconds, "0보다 커야 합니다.");
            }
            if (!(maxSeconds >= initialSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(maxSeconds), maxSeconds, "initialSeconds 이상이어야 합니다.");
            }
            if (!(jitterRatio >= 0 && jitterRatio < 1))
            {
                throw new ArgumentOutOfRangeException(nameof(jitterRatio), jitterRatio, "0 이상 1 미만이어야 합니다.");
            }

            this.initialSeconds = initialSeconds;
            this.maxSeconds = maxSeconds;
            this.jitterRatio = jitterRatio;
            if (random != null)
            {
                this.random = random;
            }
            else
            {
                var rng = new Random();
                this.random = rng.NextDouble;
            }
        }

        /// <summary>
        /// 이번 실패 뒤에 기다릴 초를 돌려주고 실패 횟수를 1 늘린다.
        /// 지터를 섞은 뒤에도 상한(maxSeconds)을 넘지 않는다.
        /// </summary>
        public double NextDelaySeconds()
        {
            double baseDelay = BaseDelaySeconds(Attempt);
            if (Attempt < int.MaxValue)
            {
                Attempt++;
            }

            if (jitterRatio <= 0)
            {
                return baseDelay;
            }

            // r ∈ [0, 1) → factor ∈ [1 - ratio, 1 + ratio)
            double r = random();
            if (double.IsNaN(r) || r < 0 || r >= 1)
            {
                r = 0.5; // 이상한 값이 오면 흔들지 않는다
            }
            double factor = 1.0 + jitterRatio * (2.0 * r - 1.0);
            return Math.Min(maxSeconds, baseDelay * factor);
        }

        /// <summary>연결에 성공했다(connected 수신). 다음 실패는 다시 첫 대기시간부터.</summary>
        public void Reset()
        {
            Attempt = 0;
        }

        /// <summary>지터 없이 계산한 n번째(0부터) 대기시간. initial × 2^n, 상한 maxSeconds.</summary>
        public double BaseDelaySeconds(int attempt)
        {
            if (attempt < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "0 이상이어야 합니다.");
            }
            int exponent = Math.Min(attempt, MaxExponent);
            double delay = initialSeconds * Math.Pow(2, exponent);
            return Math.Min(maxSeconds, delay);
        }
    }
}
