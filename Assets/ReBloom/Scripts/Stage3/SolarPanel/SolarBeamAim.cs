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
        [Tooltip("표적 중심에서 이보다 멀면 근접도 0(완전히 퍼진 상태).")]
        [Min(0.1f)] public float farDistance = 6f;

        [Tooltip("표적 중심에서 이보다 가까우면 근접도 1. 판 반너비(1.59 m) 근처로 두면 " +
                 "'거의 판에 걸친' 시점에 최대가 된다.")]
        [Min(0.05f)] public float nearDistance = 1.6f;

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

        // 빔 선분에서 가장 가까운 고장 판.
        bool TryFindNearest(out SolarReflector nearest, out float nearestDistance)
        {
            nearest = null;
            nearestDistance = float.MaxValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                SolarReflector r = candidates[i];
                if (r == null || r == self || !r.broken || !r.isActiveAndEnabled) continue;

                float d = DistanceToSegment(r.Position, beam.Origin, beam.End);
                if (d < nearestDistance)
                {
                    nearest = r;
                    nearestDistance = d;
                }
            }

            return nearest != null;
        }

        // 점과 선분의 최단 거리. 선분 밖이면 가까운 끝점까지의 거리다 —
        // 빔이 표적을 지나치기 전에 멈췄다면 "아직 못 미쳤다"가 거리로 나타나야 한다.
        static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 1e-6f) return Vector3.Distance(point, a);

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq);
            return Vector3.Distance(point, a + ab * t);
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
