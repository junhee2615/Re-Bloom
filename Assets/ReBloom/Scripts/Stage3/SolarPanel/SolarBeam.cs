using System.Collections.Generic;
using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 패널이 받은 빛을 반사시켜, 거울을 타고 이어지는 경로를 구한다.
    ///
    /// 경로는 [광원 → 이 판 → (거울 → 거울 …) → 닿은 곳]이다. 중간 거울은 씬에 놓인
    /// <see cref="SolarReflector"/>이고, 그 사이를 <b>월드 지오메트리가 가리면 거기서 끊긴다</b>.
    ///
    /// 반사 방향은 광원 방향과 판 법선만으로 정해지고, 그 둘 다 모든 피어에서 같으므로
    /// (판 각도는 <see cref="SolarPanelTilt"/>가 복제된 플레이어 위치로 계산한다)
    /// 빔은 동기화 없이 어느 피어에서나 같은 곳에 그려진다. 체인이 길어져도 마찬가지다.
    ///
    /// 배치: 광원을 직접 받는 반사면(판)과 같은 GameObject에 붙인다.
    /// 중간 거울에는 이 컴포넌트가 아니라 <see cref="SolarReflector"/>만 붙인다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Beam")]
    public class SolarBeam : MonoBehaviour
    {
        [Header("광원")]
        [Tooltip("빛을 주는 Directional Light.")]
        public Light sun;

        [Header("반사면")]
        [Tooltip("빔이 나가는 지점. 비우면 반사면의 중심(같은 오브젝트의 SolarReflector가 정하는 판 면).")]
        public Transform origin;

        [Header("레이")]
        [Tooltip("한 구간의 최대 사거리(m). 반사할 때마다 새로 적용된다.")]
        public float maxDistance = 60f;
        [Tooltip("빔을 가로막는 것들. 거울(SolarReflector)은 이 마스크와 상관없이 잡힌다.")]
        public LayerMask hitMask = ~0;
        [Tooltip("거울에서 더 튕길 수 있는 횟수. 0이면 이 판에서 한 번만 반사한다.")]
        [Range(0, 8)] public int maxBounces = 4;
        [Tooltip("판이 광원을 이 값보다 덜 정면으로 받으면 빔이 꺼진다(0~1). 중간 거울에도 같은 기준을 쓴다.")]
        [Range(0f, 1f)] public float minGain = 0.05f;

        [Header("시각화")]
        [Tooltip("빔을 그릴 LineRenderer. 없어도 계산은 동작한다.")]
        public LineRenderer line;

        /// <summary>빔이 무언가에 닿았는가(허공으로 사라지지 않았는가).</summary>
        public bool HasHit { get; private set; }

        /// <summary>최종적으로 닿은 지점(월드). <see cref="HasHit"/>가 true일 때만 유효.</summary>
        public Vector3 HitPoint { get; private set; }

        /// <summary>최종적으로 닿은 콜라이더. 거울에서 끝났으면 null.</summary>
        public Collider HitCollider { get; private set; }

        /// <summary>이 판에서 나가는 반사 방향(단위 벡터).</summary>
        public Vector3 Direction { get; private set; }

        /// <summary>이 판이 광원을 얼마나 정면으로 받는지 0~1. 0이면 빛을 못 받는다.</summary>
        public float Gain { get; private set; }

        /// <summary>체인 끝에 남은 세기. 반사할 때마다 입사각과 반사율이 곱해진다.</summary>
        public float Intensity { get; private set; }

        /// <summary>빔이 꺾인 점들. [시작점, 거울…, 끝점] 순.</summary>
        public IReadOnlyList<Vector3> Path => path;

        /// <summary>빔이 거쳐 간 거울들. 순서대로.</summary>
        public IReadOnlyList<SolarReflector> Chain => chain;

        readonly List<Vector3> path = new List<Vector3>();
        readonly List<SolarReflector> chain = new List<SolarReflector>();

        // 반사 지점에서 다시 쏠 때 자기 면을 다시 맞지 않도록 띄우는 거리.
        const float skin = 0.01f;

        // 이 판 자체가 반사체이기도 하면 첫 구간에서 제외한다.
        SolarReflector self;

        // 반사면과 법선은 같은 오브젝트의 SolarReflector를 따른다.
        // 여기서 Transform을 따로 들고 up을 쓰면 진실 공급원이 둘로 갈라진다 —
        // SolarReflector는 normalAxis로 법선 축을 고를 수 있어서, 그것을 Up이 아닌 값으로 바꾸는 순간
        // 첫 반사(여기)와 체인의 나머지 반사(SolarReflector)가 소리 없이 어긋난다.
        // 거울 없이 발광판으로만 쓰는 경우에만 자기 Transform으로 물러선다.
        Transform Surface => self != null ? self.Surface : transform;
        Vector3 Normal => self != null ? self.Normal : transform.up;

        void Reset()
        {
            line = GetComponent<LineRenderer>();
        }

        void Awake()
        {
            self = GetComponent<SolarReflector>();
        }
        
        void LateUpdate()   // 판이 Update에서 기울므로 그 뒤에 계산한다.
        {
            HasHit = false;
            HitCollider = null;
            Intensity = 0f;
            path.Clear();
            chain.Clear();

            if (sun == null)
            {
                Draw(false);
                return;
            }

            Vector3 incoming = sun.transform.forward;   // 빛이 나아가는 방향
            Vector3 normal = Normal;

            // 판이 광원을 향한 정도. 뒷면으로 받으면 0.
            Gain = Mathf.Clamp01(Vector3.Dot(normal, -incoming));
            if (Gain < minGain)
            {
                Draw(false);
                return;
            }

            Direction = Vector3.Reflect(incoming, normal).normalized;
            Intensity = Gain;

            Vector3 from = origin != null ? origin.position : Surface.position;

            path.Add(from);
            Trace(from, Direction, self);
            Draw(true);
        }

        // 거울에서 거울로 이어가며 경로를 채운다. 끝나는 경우는 셋이다.
        //  - 거울이 아닌 무언가에 막힘 → 거기가 종점
        //  - 아무것도 없는 허공 → 최대 사거리까지 그리고 종점(HasHit = false)
        //  - 더 튕길 수 없음(횟수 소진 / 뒷면 / 스치듯 맞음) → 그 거울이 종점
        void Trace(Vector3 from, Vector3 dir, SolarReflector current)
        {
            for (int bounce = 0; ; bounce++)
            {
                bool blocked = Cast(from, dir, maxDistance, current, out RaycastHit hit, out float blockDistance);
                float limit = blocked ? blockDistance : maxDistance;

                SolarReflector mirror = SolarReflector.FindNearest(from, dir, limit, current,
                                                                   out float _, out Vector3 mirrorPoint);

                if (mirror == null)
                {
                    if (blocked)
                    {
                        HasHit = true;
                        HitPoint = hit.point;
                        HitCollider = hit.collider;
                        path.Add(hit.point);
                    }
                    else
                    {
                        path.Add(from + dir * maxDistance);
                    }
                    return;
                }

                path.Add(mirrorPoint);
                chain.Add(mirror);
                mirror.MarkLit(mirrorPoint);

                if (bounce >= maxBounces ||
                    !mirror.TryReflect(dir, minGain, out Vector3 outgoing, out float gain))
                {
                    // 거울까지는 갔지만 더 가지 못한다. 그 자리가 종점이다.
                    HasHit = true;
                    HitPoint = mirrorPoint;
                    return;
                }

                Intensity *= gain * mirror.reflectance;
                from = mirrorPoint;
                dir = outgoing;
                current = mirror;
            }
        }

        // 빔을 가로막는 것이 있는지 본다. 방금 떠난 거울과 이 판 자신은 건너뛴다.
        // hitDistance는 항상 원래 from에서 잰 거리다.
        bool Cast(Vector3 from, Vector3 dir, float distance, SolarReflector ignore,
                  out RaycastHit hit, out float hitDistance)
        {
            hit = default;
            hitDistance = 0f;

            Vector3 p = from;
            float traveled = 0f;

            for (int i = 0; i < 4; i++)
            {
                float remaining = distance - traveled;
                if (remaining <= 0f) return false;

                if (!Physics.Raycast(p, dir, out hit, remaining, hitMask, QueryTriggerInteraction.Ignore))
                    return false;

                bool mine = hit.collider.transform.IsChildOf(transform) ||
                            (ignore != null && ignore.Owns(hit.collider));
                if (!mine)
                {
                    hitDistance = traveled + hit.distance;
                    return true;
                }

                traveled += hit.distance + skin;
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
