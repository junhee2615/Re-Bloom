using Fusion;
using UnityEngine;

namespace ReBloom.Electricity
{
    /// <summary>
    /// 전기 코어 미션의 박자. <b>값만 만들고 연출은 하지 않는다</b> —
    /// 빛은 <c>ElectricityLightCue</c>, 진동은 <c>ElectricityHapticCue</c>가 같은 값을 읽어 간다.
    ///
    /// 한 사이클은 <b>점점 빨라지는 박</b>으로 이뤄지고, 마지막에 한 점으로 모인다(응집).
    /// 그 순간 둘이 함께 레버를 당기면 성공이다. 실패하거나 지나가면 잠깐 쉬고 다음 사이클.
    ///
    /// <b>빛과 진동은 같은 박이다.</b> 루멘은 그 가속을 눈으로, 에코는 손으로 따라간다 —
    /// 서로의 신호는 확인할 수 없으므로 말로 맞추고 서로를 믿어야 한다.
    ///
    /// 배치: <see cref="ElectricityCoreMission"/>과 같은 GameObject(= NetworkObject 하나).
    /// </summary>
    /// <remarks>
    /// 시간을 <c>Time.time</c>이 아니라 <b><c>Runner.SimulationTime</c></b>에서 뽑는다.
    /// 로컬 시간을 쓰면 두 피어의 깜빡임과 진동이 서서히 어긋나는데, 시뮬레이션 시간은
    /// 모든 피어가 공유하므로 동기화 없이도 같은 순간을 가리킨다.
    /// </remarks>
    [AddComponentMenu("ReBloom/Electricity Pulse Clock")]
    public class ElectricityPulseClock : NetworkBehaviour
    {
        [Header("한 사이클")]
        [Tooltip("박이 빨라지다 응집까지 걸리는 시간(초).")]
        [Min(1f)] public float cycleDuration = 6f;

        [Tooltip("응집 뒤 다음 사이클까지 쉬는 시간(초). 숨 돌리고 다시 준비하는 구간.")]
        [Min(0.5f)] public float restDuration = 2.5f;

        [Tooltip("응집 전까지 몇 번 박이 오는가.")]
        [Range(3, 32)] public int beatCount = 10;

        [Tooltip("클수록 뒤에서 급격히 빨라진다. 1이면 일정한 간격.")]
        [Range(1f, 4f)] public float accelPower = 2f;

        [Tooltip("가속 중인 박 하나가 켜져 있는 길이(초). 깜빡임·진동 펄스에 함께 쓴다.")]
        [Min(0.02f)] public float beatDuration = 0.08f;

        [Tooltip("응집 순간의 신호가 켜져 있는 길이(초). 가속 중의 박보다 길게 둬야 " +
                 "빨라진 깜빡임에 묻히지 않고 '여기다'가 분명히 보인다. " +
                 "판정 창보다 길면 불은 켜져 있는데 판정은 닫힌 구간이 생기므로 보통 창과 같게 둔다.")]
        [Min(0.05f)] public float convergeHoldDuration = 0.25f;

        [Header("응집 판정")]
        [Tooltip("응집 순간에서 이 시간(초) 안에 당기면 맞춘 것으로 본다.")]
        [Min(0.02f)] public float convergeWindow = 0.25f;

        /// <summary>
        /// 박자가 돌고 있는가. <see cref="ElectricityCoreMission"/>이 켜고 끈다 —
        /// 미션이 시작되기 전과 클리어된 뒤에는 꺼져 있어야 한다.
        /// </summary>
        public bool Running { get; private set; }

        /// <summary>박자의 0점(초). 미션이 시작한 시각이다.</summary>
        public double Origin { get; private set; }

        /// <summary>박자를 켜거나 끈다. 켤 때의 시각이 0점이 되므로 매 시도의 흐름이 늘 같다.</summary>
        public void SetRunning(bool running, double origin)
        {
            Running = running;
            Origin = origin;
        }

        /// <summary>모든 피어가 공유하는 현재 시각(초).</summary>
        public double Now => Runner != null ? (double)Runner.SimulationTime : 0d;

        /// <summary>박자가 시작된 뒤 흐른 시간(초). 꺼져 있으면 음수.</summary>
        public double Elapsed => Running ? Now - Origin : -1d;

        /// <summary>사이클 전체 길이(가속 + 쉼).</summary>
        public float TotalDuration => cycleDuration + restDuration;

        /// <summary>지금 빛이 켜져 있는가.</summary>
        public bool LightOn => Running && IsBeatOn(Elapsed);

        /// <summary>지금 진동이 울리는가. 빛과 <b>같은 박</b>이다 — 감각만 다르다.</summary>
        public bool BuzzOn => Running && IsBeatOn(Elapsed);

        /// <summary>지금이 응집 순간인가. 레버를 당겨야 하는 때.</summary>
        public bool IsConverged => Running && IsConvergedAt(Elapsed);

        /// <summary>응집까지 얼마나 왔는가 0~1. 1이면 그 순간. 연출이 이 값으로 세기를 올린다.</summary>
        public float Convergence01
        {
            get
            {
                if (!Running) return 0f;

                float inCycle = InCycle(Elapsed);

                if (inCycle <= cycleDuration) return Mathf.Clamp01(inCycle / cycleDuration);

                // 응집을 붙잡고 있는 동안은 1을 유지한다. 안 그러면 제일 중요한 순간에
                // 밝기가 흐려진 채로 켜져 있다.
                if (inCycle <= cycleDuration + convergeHoldDuration) return 1f;

                return 0f;   // 쉬는 구간
            }
        }

        /// <summary>주어진 경과 시각이 응집 창 안인가. 미션이 판정에 쓴다.</summary>
        public bool IsConvergedAt(double elapsed) => Mathf.Abs(SignedToConvergence(elapsed)) <= convergeWindow;

        /// <summary>응집까지 남은 시간(초). 음수면 이미 지났다.</summary>
        public float SecondsToConvergence => -SignedToConvergence(Elapsed);

        // 응집 순간 기준 부호 있는 거리. 응집 전이면 음수, 후면 양수.
        float SignedToConvergence(double elapsed)
        {
            if (elapsed < 0d) return float.MaxValue;

            float d = InCycle(elapsed) - cycleDuration;

            // 다음 사이클 응집이 더 가까우면 그쪽을 본다(쉬는 구간 후반).
            if (d > restDuration * 0.5f) d -= TotalDuration;

            return d;
        }

        // 사이클 안에서의 위치(0 ~ TotalDuration).
        float InCycle(double elapsed)
        {
            double total = TotalDuration;
            return (float)(elapsed - System.Math.Floor(elapsed / total) * total);
        }

        /// <summary>
        /// 지금 박이 켜져 있는가. 박은 <see cref="accelPower"/>를 따라 뒤로 갈수록 촘촘해지고,
        /// 마지막 박이 곧 응집이다.
        /// </summary>
        bool IsBeatOn(double elapsed)
        {
            if (elapsed < 0d) return false;

            float inCycle = InCycle(elapsed);
            if (inCycle > cycleDuration + convergeHoldDuration) return false;   // 쉬는 구간

            for (int i = 0; i <= beatCount; i++)
            {
                float at = BeatTime(i);
                if (inCycle < at) return false;                 // 아직 다음 박 전 — 더 볼 필요 없다

                // 마지막 박(= 응집)만 오래 붙잡는다. 가속 끝에서는 간격이 0.2초 아래라
                // 같은 길이로 두면 "삐—" 하는 블러에 묻혀 어느 게 응집인지 보이지 않는다.
                float dur = i == beatCount ? convergeHoldDuration : beatDuration;
                if (inCycle - at < dur) return true;
            }

            return false;
        }

        /// <summary>i번째 박이 오는 시각(사이클 기준, 초). <c>i == beatCount</c>가 응집이다.</summary>
        public float BeatTime(int i)
        {
            float x = Mathf.Clamp01((float)i / beatCount);

            // 1-(1-x)^p : 뒤로 갈수록 간격이 좁아진다. p=1이면 일정 간격.
            return cycleDuration * (1f - Mathf.Pow(1f - x, accelPower));
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !Running) return;

            Gizmos.color = IsConverged ? new Color(1f, 0.95f, 0.4f, 0.9f)
                                       : new Color(0.4f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, 0.3f + Convergence01 * 0.5f);
        }
    }
}
