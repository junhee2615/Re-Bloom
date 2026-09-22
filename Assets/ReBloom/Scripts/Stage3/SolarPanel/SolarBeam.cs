using System.Collections.Generic;
using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 패널이 받은 빛을 반사시켜, 거울을 타고 이어지는 경로를 구한다.
    /// 배치: 광원을 직접 받는 반사면(판)과 같은 GameObject에 붙인다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Beam")]
    public class SolarBeam : MonoBehaviour
    {
        static Light Sun => RenderSettings.sun;

        bool warnedNoSun;

        [Header("레이")]
        [Tooltip("한 구간의 최대 사거리(m). 반사할 때마다 새로 적용된다.")]
        public float maxDistance = 60f;
        [Tooltip("빔이 맞는 것들.")]
        public LayerMask hitMask = ~0;
        [Tooltip("거울에서 더 튕길 수 있는 횟수.")]
        [Range(0, 8)] public int maxBounces = 4;
        [Tooltip("판이 광원을 이 값보다 덜 정면으로 받으면 빔이 꺼진다(0~1). 중간 거울에도 같은 기준을 쓴다.")]
        [Range(0f, 1f)] public float minGain = 0.05f;

        [Header("조작감 과장")]
        [Tooltip("앞으로 기울일 때 기울기가 빔에 반영되는 배율. 1 = 실제 반사")]
        [Min(0f)] public float tiltGainForward = 1f;
        [Tooltip("뒤로 기울일 때 배율. 1 = 실제 반사.")]
        [Min(0f)] public float tiltGainBack = 1f;

        /// <summary>빔이 무언가에 닿았는가.</summary>
        public bool HasHit { get; private set; }
        /// <summary>최종적으로 닿은 지점(월드). <see cref="HasHit"/>가 true일 때만 유효.</summary>
        public Vector3 HitPoint { get; private set; }
        /// <summary>최종적으로 닿은 콜라이더. 거울에서 끝났으면 null.</summary>
        public Collider HitCollider { get; private set; }
        /// <summary>이 판에서 나가는 반사 방향(단위 벡터).</summary>
        public Vector3 Direction { get; private set; }
        /// <summary>이 판이 광원을 얼마나 정면으로 받는지 0~1. 0이면 빛을 못 받는다.</summary>
        public float Gain { get; private set; }

        /// <summary>빔이 꺾인 점들.</summary>
        public IReadOnlyList<Vector3> Path => path;
        /// <summary>빔이 거쳐 간 거울들. 순서대로.</summary>
        public IReadOnlyList<SolarReflector> Chain => chain;

        readonly List<Vector3> path = new List<Vector3>();
        readonly List<SolarReflector> chain = new List<SolarReflector>();

        // 레이를 표면에서 살짝 띄워 쏘는 거리.
        const float skin = 0.01f;

        // 자기 판의 콜라이더를 건너뛰는 최대 횟수. 판 하나의 콜라이더 수보다 넉넉하면 된다.
        const int maxSelfSkips = 4;

        // 빔을 그릴 LineRenderer.
        LineRenderer line;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
        }


        void LateUpdate()   // 판이 Update에서 기울므로 그 뒤에 계산한다.
        {
            HasHit = false;
            HitCollider = null;
            path.Clear();
            chain.Clear();

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
            Vector3 normal = transform.up;                // 판 법선.

            // ── 게임용 과장. 실제 반사가 아니라 조작감을 위한 손질이다. ──
            // 배율을 앞뒤로 따로 둔다.
            //
            // 판은 pitch로만 움직인다
            // 판의 좌우 축이라 축을 고정하고, 앞뒤는 각도의 부호로 가린다.
            Vector3 hinge = transform.right;
            float pitch = Vector3.SignedAngle(Vector3.up, normal, hinge);   // + 앞으로 기움, − 뒤로 기움
            float gain = pitch >= 0f ? tiltGainForward : tiltGainBack;

            if (!Mathf.Approximately(gain, 1f))
                normal = Quaternion.AngleAxis(pitch * gain, hinge) * Vector3.up; // up 벡터를 hinge 축 중심으로 pitch × gain 도만큼 돌림

            // 판이 광원을 향한 정도. 뒷면으로 받으면 0.
            Gain = Mathf.Clamp01(Vector3.Dot(normal, -incoming));
            if (Gain < minGain)
            {
                Draw(false);
                return;
            }

            Direction = Vector3.Reflect(incoming, normal).normalized;

            Vector3 from = transform.position;

            path.Add(from);
            Trace(from, Direction);
            Draw(true);
        }

        // 거울에서 거울로 이어가며 경로를 채운다. 끝나는 경우는 셋이다.
        //  - 아무것도 없는 허공 → 최대 사거리까지 그리고 종점(HasHit = false)
        //  - 거울이 아닌 것에 막힘 → 거기가 종점.
        //  - 거울 앞면에 닿았지만 더 튕길 수 없음(횟수 소진 / 고장 / 스치듯 맞음) → 그 거울이 종점(HitCollider = null)
        void Trace(Vector3 from, Vector3 dir)
        {
            for (int bounce = 0; ; bounce++)
            {
                if (!Cast(from, dir, out RaycastHit hit))
                {
                    path.Add(from + dir * maxDistance);
                    return;
                }

                path.Add(hit.point);
                HasHit = true;
                HitPoint = hit.point;

                // 맞은 것이 거울인가 — 거울 컴포넌트가 있고, 그 콜라이더의 앞면에 맞았을 때만이다.
                if (!SolarReflector.TryGet(hit.collider, out SolarReflector mirror) || !mirror.IsFrontFace(hit.normal))
                {
                    HitCollider = hit.collider;
                    return;
                }

                chain.Add(mirror);
                mirror.MarkLit(hit.point);

                if (bounce >= maxBounces || !mirror.TryReflect(dir, minGain, out Vector3 outgoing))
                    return;   // 거울까지는 갔지만 더 가지 못한다. 그 자리가 종점이다.

                from = hit.point;
                dir = outgoing;
            }
        }

        // 이 구간에서 빔이 처음 맞는 것. 자기 판의 콜라이더는 건너뛴다 — 빔이 판 면 안에서 출발하기 때문.
        // 레이는 항상 skin만큼 띄워 쏘므로, 반사 지점(방금 떠난 거울 표면)에서 시작해도 그 면을 다시 맞지 않는다.
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

            bool show = visible && path.Count >= 2;
            line.enabled = show;
            if (!show) return;

            line.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++)
                line.SetPosition(i, path[i]);
        }

        void OnDrawGizmos()
        {
            if (!Application.isPlaying || path.Count < 2) return;

            Gizmos.color = HasHit ? new Color(1f, 0.95f, 0.5f, 0.9f) : new Color(1f, 0.6f, 0.2f, 0.4f);
            for (int i = 1; i < path.Count; i++)
                Gizmos.DrawLine(path[i - 1], path[i]);

            if (HasHit) Gizmos.DrawWireSphere(HitPoint, 0.1f);
        }
    }
}
