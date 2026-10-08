using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 판이 받은 햇빛을 한 번 반사시켜 어디로 가는지 구한다.
    ///
    /// <b>중계는 하지 않는다.</b> 빔은 처음 맞은 것에서 끝난다 — 맞은 것이 고장난 거울이면
    /// <see cref="SolarReflector.MarkLit"/>로 빛을 표시하고 거기서 멈춘다. 거울을 타고 이어지는
    /// 체인은 걷어냈다: 누워 있는 pitch 전용 판으로는 수평 빔을 수평으로 중계할 수 없어
    /// (반사가 대칭 연산이라 판이 수직이어야 한다) 원리적으로 성립하지 않았다.
    /// 대신 <b>판마다 이 컴포넌트를 하나씩</b> 두어 각자 태양을 직접 받는다.
    ///
    /// 고장난 판(<see cref="SolarReflector.broken"/>)은 빔을 내지 않는다. 수리되면 켜진다 —
    /// 되살아난 태양광 패널이 다시 발전을 시작하는 셈이다.
    ///
    /// 반사 방향은 광원 방향과 판 법선만으로 정해지고 그 둘 다 모든 피어에서 같으므로
    /// (판 각도는 <see cref="SolarPanelTilt"/>가 복제된 플레이어 위치로 계산한다) 동기화하지 않는다.
    ///
    /// 배치: 반사면(판 면)과 같은 GameObject.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Beam")]
    public class SolarBeam : MonoBehaviour
    {
        static Light Sun => RenderSettings.sun;

        bool warnedNoSun;

        [Header("레이")]
        [Tooltip("빔의 최대 사거리(m). 이 안에 아무것도 없으면 허공으로 사라진다.")]
        public float maxDistance = 60f;
        [Tooltip("빔이 맞는 것들. 거울의 콜라이더도 이 마스크 안에 있어야 한다.")]
        public LayerMask hitMask = ~0;
        [Tooltip("판이 광원을 이 값보다 덜 정면으로 받으면 빔이 꺼진다(0~1).")]
        [Range(0f, 1f)] public float minGain = 0.05f;

        [Header("조작감 과장")]
        [Tooltip("앞으로 기울일 때 기울기가 빔에 반영되는 배율. 1 = 실제 반사")]
        [Min(0f)] public float tiltGainForward = 1f;
        [Tooltip("뒤로 기울일 때 배율. 1 = 실제 반사.")]
        [Min(0f)] public float tiltGainBack = 1f;

        /// <summary>지금 빔이 나오고 있는가. 고장났거나 해를 등지면 false.</summary>
        public bool IsEmitting { get; private set; }

        /// <summary>빔이 무언가에 닿았는가(허공으로 사라지지 않았는가).</summary>
        public bool HasHit { get; private set; }

        /// <summary>닿은 지점(월드). <see cref="HasHit"/>가 true일 때만 유효.</summary>
        public Vector3 HitPoint { get; private set; }

        /// <summary>닿은 콜라이더. 거울 앞면에서 끝났으면 null(<see cref="HitMirror"/>를 볼 것).</summary>
        public Collider HitCollider { get; private set; }

        /// <summary>빔이 앞면으로 닿은 거울. 거울이 아닌 것에 막혔거나 허공이면 null.</summary>
        public SolarReflector HitMirror { get; private set; }

        /// <summary>이 판에서 나가는 반사 방향(단위 벡터).</summary>
        public Vector3 Direction { get; private set; }

        /// <summary>이 판이 광원을 얼마나 정면으로 받는지 0~1. 0이면 빛을 못 받는다.</summary>
        public float Gain { get; private set; }

        /// <summary>빔의 출발점(= 판 면 중심). <see cref="IsEmitting"/>일 때만 유효.</summary>
        public Vector3 Origin { get; private set; }

        /// <summary>빔의 끝점. 닿은 곳이거나, 허공이면 최대 사거리 지점.</summary>
        public Vector3 End { get; private set; }

        // 레이를 표면에서 살짝 띄워 쏘는 거리. 출발하자마자 자기 면을 맞는 것을 피한다.
        const float skin = 0.01f;

        // 자기 판의 콜라이더를 건너뛰는 최대 횟수. 판 하나의 콜라이더 수보다 넉넉하면 된다.
        const int maxSelfSkips = 4;

        // 이 판의 거울. 고장이면 빔을 내지 않는다.
        SolarReflector self;

        // 빔을 그릴 LineRenderer.
        LineRenderer line;

        void Awake()
        {
            self = GetComponent<SolarReflector>();
            line = GetComponent<LineRenderer>();
        }

        void LateUpdate()   // 판이 Update에서 기울므로 그 뒤에 계산한다.
        {
            IsEmitting = false;
            HasHit = false;
            HitCollider = null;
            HitMirror = null;

            // 고장난 판은 발전하지 못한다. 수리되면 이 가드가 풀려 빔이 켜진다.
            if (self != null && self.broken)
            {
                Draw(false);
                return;
            }

            Light light = Sun;
            if (light == null)
            {
                if (!warnedNoSun)
                {
                    warnedNoSun = true;
                    Debug.LogWarning($"[SolarBeam] {name}: 광원이 없어 빔을 그리지 않습니다. " +
                                     $"Lighting > Environment > Sun Source를 설정하세요.", this);
                }
                Draw(false);
                return;
            }

            Vector3 incoming = light.transform.forward;   // 빛이 나아가는 방향
            Vector3 normal = transform.up;                // 판 법선

            // ── 게임용 과장. 실제 반사가 아니라 조작감을 위한 손질이다. ──
            // 배율을 앞뒤로 따로 둔다. 해가 비스듬해서 반사가 비대칭이라(해 쪽으로 세우는 방향은 급하고
            // 등 돌리는 방향은 완만) 느린 쪽만 키워야 양쪽 도달 거리가 맞는다.
            //
            // 판은 pitch로만 움직이므로(SolarPanelTilt에 roll이 없다) 경첩은 항상 판의 좌우 축이다.
            // 축을 고정하고 앞뒤는 각도의 부호로 가린다.
            Vector3 hinge = transform.right;
            float pitch = Vector3.SignedAngle(Vector3.up, normal, hinge);   // + 앞으로 기움, − 뒤로 기움
            float gain = pitch >= 0f ? tiltGainForward : tiltGainBack;

            if (!Mathf.Approximately(gain, 1f))
                normal = Quaternion.AngleAxis(pitch * gain, hinge) * Vector3.up;

            // 판이 광원을 향한 정도. 뒷면으로 받으면 0.
            Gain = Mathf.Clamp01(Vector3.Dot(normal, -incoming));
            if (Gain < minGain)
            {
                Draw(false);
                return;
            }

            IsEmitting = true;
            Direction = Vector3.Reflect(incoming, normal).normalized;
            Origin = transform.position;
            End = Trace(Origin, Direction);

            Draw(true);
        }

        // 빔이 처음 맞는 것을 찾아 끝점을 준다. 끝나는 경우는 셋이다.
        //  - 아무것도 없는 허공 → 최대 사거리 지점(HasHit = false)
        //  - 거울 앞면 → HitMirror에 담고 MarkLit. 그 자리가 끝이다(중계하지 않는다)
        //  - 그 밖의 무언가 → HitCollider에 담는다. 거울의 옆면·뒷면·꺼진 거울도 여기 든다
        Vector3 Trace(Vector3 from, Vector3 dir)
        {
            if (!Cast(from, dir, out RaycastHit hit))
                return from + dir * maxDistance;

            HasHit = true;
            HitPoint = hit.point;

            // 맞은 것이 거울인가 — 거울 컴포넌트가 있고, 그 콜라이더의 앞면에 맞았을 때만이다.
            if (SolarReflector.TryGet(hit.collider, out SolarReflector mirror) && mirror.IsFrontFace(hit.normal))
            {
                HitMirror = mirror;
                mirror.MarkLit(hit.point);
            }
            else
            {
                HitCollider = hit.collider;
            }

            return hit.point;
        }

        // 자기 판의 콜라이더는 건너뛴다 — 빔이 판 면 안에서 출발하기 때문이다.
        bool Cast(Vector3 from, Vector3 dir, out RaycastHit hit)
        {
            hit = default;

            Vector3 p = from + dir * skin;
            float remaining = maxDistance - skin;

            for (int i = 0; i < maxSelfSkips; i++)
            {
                if (remaining <= 0f) return false;

                if (!Physics.Raycast(p, dir, out hit, remaining, hitMask, QueryTriggerInteraction.Ignore))
                    return false;

                if (!hit.collider.transform.IsChildOf(transform)) // 방금 맞은 콜라이더가 내 것인가
                    return true;

                remaining -= hit.distance + skin;
                p = hit.point + dir * skin;
            }

            return false;
        }

        void Draw(bool visible)
        {
            if (line == null) return;

            line.enabled = visible;
            if (!visible) return;

            line.positionCount = 2;
            line.SetPosition(0, Origin);
            line.SetPosition(1, End);
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || !IsEmitting) return;

            Gizmos.color = HasHit ? new Color(1f, 0.95f, 0.5f, 0.9f) : new Color(1f, 0.6f, 0.2f, 0.4f);
            Gizmos.DrawLine(Origin, End);

            if (HasHit) Gizmos.DrawWireSphere(HitPoint, 0.1f);
        }
    }
}
