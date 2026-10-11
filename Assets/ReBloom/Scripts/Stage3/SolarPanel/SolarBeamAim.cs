using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 이 판의 빔이 "살려야 할 판"을 얼마나 정확히 겨누고 있는지 0~1로 잰다.
    /// 값만 만들고 연출은 하지 않는다 — 진동은 <see cref="SolarBeamHaptics"/>가, 소리·UI가 붙으면 같은 값을 읽는다.
    ///
    /// 재는 값은 빔이 <b>닿은 지점</b>이 아니라 <b>빔 선분과 표적 중심의 최단 거리</b>다.
    /// 닿은 지점으로 재면 빗나간 빔이 하늘로 사라지는 순간 거리가 수십 m로 튀어
    /// "가까워지는 중"이 드러나지 않는다. 최단 거리는 빔이 표적을 스쳐 지나가도 연속적으로 줄었다 늘어난다.
    ///
    /// 단 <b>빔 뒤에 있는 판은 세지 않는다.</b> 판 격자가 촘촘해(열 4 m / 줄 5.1 m) 어느 방향을 겨눠도
    /// 이웃이 거리 안에 들어오므로, 뒤까지 세면 엉뚱한 방향을 겨눠도 진동이 올라가 있다.
    /// 같은 이유로 <see cref="farDistance"/>는 <b>열 간격보다 좁아야</b> 한다 — pitch 전용 설계상
    /// 반사광의 x는 태양의 x로 고정돼(z 1 m당 x 0.124 m) <b>다른 열은 영영 맞출 수 없는데</b>,
    /// 범위가 넓으면 그 판까지 표적으로 잡힌다.
    ///
    /// <b>조용한 경우는 이 판이 고장났을 때뿐이다.</b> 겨눌 표적이 없어도, 빔이 안 나와도
    /// <see cref="AimState.Scattered"/>로 둔다 — "여기서는 아무 일도 일어나지 않는다"를 약한 진동으로
    /// 계속 알려주는 쪽이, 침묵으로 "고장인지 아닌지" 헷갈리게 하는 것보다 낫다.
    ///
    /// 표적은 <b>아직 고장난</b> 거울이다(자기 자신은 제외). 수리되면 표적에서 빠지고 다음 고장 판이 표적이 된다.
    ///
    /// 배치: <see cref="SolarBeam"/>과 같은 GameObject(= 판 면).
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Beam Aim")]
    [RequireComponent(typeof(SolarBeam))]
    [DefaultExecutionOrder(200)]   // SolarBeam(기본 0)의 LateUpdate보다 뒤에서 읽는다.
    public class SolarBeamAim : MonoBehaviour
    {
        /// <summary>조준 상태. 연출이 갈리는 지점이다.</summary>
        public enum AimState
        {
            /// <summary>이 판이 고장나 발전하지 못한다. 유일하게 조용한 상태다.</summary>
            Off,
            /// <summary>겨눌 표적이 없거나 멀다. "빛이 퍼진 상태".</summary>
            Scattered,
            /// <summary>가까워지는 중.</summary>
            Converging,
            /// <summary>빔이 실제로 고장 판에 닿았다.</summary>
            OnTarget,
        }

        [Header("거리 → 근접도")]
        [Tooltip("빔에서 이보다 먼 고장 판은 근접도 0(완전히 퍼진 상태). " +
                 "판 열 간격(4 m)보다 좁게 둘 것 — 넓으면 pitch 전용 설계상 영영 맞출 수 없는 " +
                 "다른 열 판까지 표적으로 잡혀 진동이 거짓으로 올라간다.")]
        [Min(0.1f)] public float farDistance = 3f;

        [Tooltip("빔에서 이보다 가까우면 근접도 1. 빗나감은 대부분 상하(기울기)이므로 " +
                 "판 반높이(0.96 m) 근처로 둔다.")]
        [Min(0.05f)] public float nearDistance = 1f;

        [Header("표적")]
        [Tooltip("비우면 씬의 모든 SolarReflector를 찾아 쓴다. 그중 broken인 것만 표적이 된다.")]
        public SolarReflector[] candidates;

        /// <summary>지금 겨누고 있는(가장 가까운) 고장 판. 없으면 null.</summary>
        public SolarReflector Target { get; private set; }

        /// <summary>표적 중심까지의 최단 거리(m). <see cref="Target"/>이 null이면 의미 없다.</summary>
        public float Distance { get; private set; }

        /// <summary>0 = 완전히 빗나감, 1 = 표적에 닿음. 연출이 읽는 값이다.</summary>
        public float Proximity { get; private set; }

        /// <summary>지금 조준 상태.</summary>
        public AimState State { get; private set; }

        SolarReflector self;
        SolarBeam beam;

        void Awake()
        {
            self = GetComponent<SolarReflector>();
            beam = GetComponent<SolarBeam>();

            if (candidates == null || candidates.Length == 0)
                candidates = FindObjectsByType<SolarReflector>(FindObjectsSortMode.None);
        }

        void LateUpdate()
        {
            Target = null;
            Distance = 0f;
            Proximity = 0f;

            // 이 판이 고장이면 아무것도 하지 않는다. 그 밖에는 최소 Scattered다.
            if (self != null && self.broken)
            {
                State = AimState.Off;
                return;
            }

            State = AimState.Scattered;

            // 빔이 안 나오면(해를 등졌거나 광원이 없으면) 겨눌 방향 자체가 없다.
            if (beam == null || !beam.IsEmitting) return;

            // 빔이 고장 판 앞면에 닿았으면 그게 곧 정답이다. 거리를 잴 필요가 없다.
            SolarReflector hit = beam.HitMirror;
            if (hit != null && hit != self && hit.broken)
            {
                Target = hit;
                Proximity = 1f;
                State = AimState.OnTarget;
                return;
            }

            if (!TryFindNearest(out SolarReflector nearest, out float distance)) return;

            Target = nearest;
            Distance = distance;
            Proximity = 1f - Mathf.Clamp01(Mathf.InverseLerp(Mathf.Min(nearDistance, farDistance),
                                                             farDistance, distance));
            if (Proximity > 0f) State = AimState.Converging;
        }

        // 빔이 겨누고 있는 것 중 가장 가까운 고장 판. 빔 뒤에 있는 판은 세지 않는다.
        bool TryFindNearest(out SolarReflector nearest, out float nearestDistance)
        {
            nearest = null;
            nearestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                SolarReflector r = candidates[i];
                if (r == null || r == self || !r.broken || !r.isActiveAndEnabled) continue;

                if (!TryDistanceAhead(r.Position, beam.Origin, beam.End, out float d)) continue;

                if (d < nearestDistance)
                {
                    nearest = r;
                    nearestDistance = d;
                }
            }

            return nearest != null;
        }

        // 빔 선분과 점의 최단 거리. "겨누고 있는가"를 재는 것이므로 두 가지를 구분한다.
        //
        //  - 점이 빔 <b>뒤</b>에 있으면 false. 겨누는 방향과 무관하게 옆·뒤의 판이 늘 걸려
        //    (격자가 촘촘해서 반드시 걸린다) 엉뚱한 방향을 겨눠도 진동이 올라가 있었다.
        //  - 점이 빔 <b>앞</b>이지만 선분보다 멀면 끝점까지의 거리를 쓴다 — 빔이 표적에 못 미치고
        //    멈췄다면 "아직 못 미쳤다"가 거리로 나타나야 한다.
        static bool TryDistanceAhead(Vector3 point, Vector3 a, Vector3 b, out float distance)
        {
            distance = 0f;

            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-6f) return false;

            float t = Vector3.Dot(point - a, ab) / lengthSq;
            if (t < 0f) return false;   // 빔 뒤 — 겨누고 있지 않다

            distance = Vector3.Distance(point, a + ab * Mathf.Min(t, 1f));
            return true;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || Target == null) return;

            Gizmos.color = State == AimState.OnTarget
                ? new Color(0.3f, 1f, 0.5f, 0.9f)
                : Color.Lerp(new Color(1f, 0.4f, 0.2f, 0.6f), new Color(1f, 0.95f, 0.4f, 0.9f), Proximity);

            Gizmos.DrawWireSphere(Target.Position, nearDistance);
            if (beam != null && beam.IsEmitting) Gizmos.DrawLine(Target.Position, beam.End);
        }
    }
}
