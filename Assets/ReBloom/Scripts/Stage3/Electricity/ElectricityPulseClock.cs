using Fusion;
using UnityEngine;

namespace ReBloom.Electricity
{
    /// <summary>
    /// 전기 코어 미션의 박자. <b>값만 만들고 연출은 하지 않는다</b> —
    /// 빛은 <c>ElectricityLightCue</c>, 진동은 <c>ElectricityHapticCue</c>가 같은 값을 읽어 간다.
    ///
    /// 두 박자의 <b>주기가 다르다</b>(폴리리듬). 그래서 빛과 진동이 겹치는 순간이 드물게 찾아오고,
    /// 그때가 레버를 당길 타이밍이다. 한쪽 감각만으로는 "이번이 그 순간인지" 알 수 없어
    /// 서로 말로 맞춰야 한다 — 이 미션이 협동이 되는 지점이다.
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
        [Header("박자 (서로 나누어떨어지지 않게 둘 것)")]
        [Tooltip("빛이 깜빡이는 주기(초). mental이 보는 쪽.")]
        [Min(0.2f)] public float lightPeriod = 3f;

        [Tooltip("진동이 울리는 주기(초). ear가 느끼는 쪽.")]
        [Min(0.2f)] public float buzzPeriod = 5f;

        [Tooltip("한 박이 켜져 있는 길이(초). 깜빡임·진동 펄스의 길이로 함께 쓴다.")]
        [Min(0.02f)] public float beatDuration = 0.12f;

        [Header("응집 판정")]
        [Tooltip("두 박 사이가 이 시간(초) 안이면 '겹쳤다'고 본다. 레버 판정 창의 기준.")]
        [Min(0.02f)] public float convergeWindow = 0.25f;

        [Tooltip("연출용 근접도를 0으로 볼 간격(초). 두 박이 이보다 멀면 Convergence01 = 0.")]
        [Min(0.1f)] public float scatterGap = 1.5f;

        /// <summary>
        /// 박자가 돌고 있는가. <see cref="ElectricityCoreMission"/>이 켜고 끈다 —
        /// 미션이 시작되기 전과 클리어된 뒤에는 꺼져 있어야 한다.
        /// </summary>
        public bool Running { get; private set; }

        /// <summary>박자의 0점(초). 미션이 시작한 시각이다.</summary>
        public double Origin { get; private set; }

        /// <summary>박자를 켜거나 끈다. 켤 때의 시각이 0점이 되므로 첫 겹침까지의 시간이 늘 같다.</summary>
        public void SetRunning(bool running, double origin)
        {
            Running = running;
            Origin = origin;
        }

        /// <summary>모든 피어가 공유하는 현재 시각(초).</summary>
        public double Now => Runner != null ? (double)Runner.SimulationTime : 0d;

        /// <summary>박자가 시작된 뒤 흐른 시간(초). 꺼져 있으면 음수.</summary>
        public double Elapsed => Running ? Now - Origin : -1d;

        /// <summary>지금 빛이 켜져 있는가.</summary>
        public bool LightOn => Running && IsBeatOn(Elapsed, lightPeriod);

        /// <summary>지금 진동이 울리는가.</summary>
        public bool BuzzOn => Running && IsBeatOn(Elapsed, buzzPeriod);

        /// <summary>두 박이 겹쳤는가. 레버를 당겨야 하는 순간.</summary>
        public bool IsConverged => Running && BeatGap(Elapsed) <= convergeWindow;

        /// <summary>두 박이 얼마나 가까운가 0~1. 1이면 겹친 순간. 연출이 이 값으로 세기를 올린다.</summary>
        public float Convergence01
        {
            get
            {
                if (!Running) return 0f;

                float gap = BeatGap(Elapsed);
                if (gap >= scatterGap) return 0f;
                return 1f - Mathf.Clamp01(gap / scatterGap);
            }
        }

        /// <summary>주어진 시각에 그 주기의 박이 켜져 있는가.</summary>
        static bool IsBeatOn(double t, float period) => Phase(t, period) < 0.12f;

        /// <summary>박 시작점에서 얼마나 지났는가(초).</summary>
        static double Phase(double t, float period) => t - System.Math.Floor(t / period) * period;

        /// <summary>
        /// 지금 시점에서 가장 가까운 빛 박과 진동 박이 서로 얼마나 떨어져 있는가(초).
        /// 겹치면 0에 가깝다. 주기를 훑지 않고 각자의 최근접 박만 구해 빼면 되므로 비용이 없다.
        /// </summary>
        public float BeatGap(double t)
        {
            return Mathf.Abs((float)(NearestBeat(t, lightPeriod) - NearestBeat(t, buzzPeriod)));
        }

        // t에서 가장 가까운 박의 시각. 직전 박과 다음 박 중 가까운 쪽.
        static double NearestBeat(double t, float period)
        {
            double prev = System.Math.Floor(t / period) * period;
            double next = prev + period;
            return (t - prev) <= (next - t) ? prev : next;
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
