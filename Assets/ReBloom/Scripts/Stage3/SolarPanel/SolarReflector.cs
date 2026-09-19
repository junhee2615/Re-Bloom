using System.Collections.Generic;
using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 빔을 한 번 더 튕겨 보내는 거울면. 씬에 배치된 태양광 패널에 붙인다.
    ///
    /// 콜라이더를 쓰지 않고 <b>사각형 평면</b>과의 교차를 직접 푼다. 이유가 둘이다.
    ///  - 패널 프리팹(BrokenSolarPanel_low / NormalSolarPanel_low)에는 콜라이더가 없다.
    ///    모델에 맞춰 박스를 붙여도 판 면과 미묘하게 어긋나 반사각이 틀어진다.
    ///  - 반사 지점이 물리 형상이 아니라 <b>설계한 사각형</b>으로 정해져야 퍼즐 난이도를 잡을 수 있다.
    ///
    /// 월드 지오메트리에 가려지는지는 <see cref="SolarBeam"/>이 물리 레이로 따로 본다.
    /// 반사에 쓰는 값이 전부 Transform과 광원 방향뿐이라 모든 피어가 같은 결과를 얻는다 — 동기화하지 않는다.
    ///
    /// 배치: 패널 루트(또는 판 면을 나타내는 자식)에 붙이고, 판 면의 법선 축과 크기를 맞춘다.
    /// 지금 못 쓰는 거울(깨진 패널 등)은 이 컴포넌트를 <b>비활성</b>하면 빔이 그냥 통과한다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Reflector")]
    public class SolarReflector : MonoBehaviour
    {
        /// <summary>판 면의 법선으로 쓸 로컬 축.</summary>
        public enum Axis { Up, Down, Forward, Back, Right, Left }

        [Header("반사면")]
        [Tooltip("반사면으로 쓸 Transform. 비우면 이 오브젝트. 기우는 판이면 회전하는 쪽을 넣는다.")]
        public Transform surface;

        [Tooltip("판 면의 법선 방향(surface 기준 로컬 축).")]
        public Axis normalAxis = Axis.Up;

        [Tooltip("거울 사각형의 반너비/반높이, 단위 m(월드). 이 안에 맞아야 반사한다. 스케일과 무관하다.")]
        public Vector2 halfExtents = new Vector2(1f, 1f);

        [Tooltip("뒷면으로 맞아도 반사할지. 끄면 뒷면에 맞은 빔은 여기서 멈춘다(판이 가린 것).")]
        public bool twoSided;

        [Tooltip("한 번 튕길 때마다 빔 세기에 곱해지는 값. 낡은 패널을 약하게 만들 때 쓴다.")]
        [Range(0f, 1f)] public float reflectance = 1f;

        [Header("고장")]
        [Tooltip("고장난 상태. 빛은 받지만(IsLit) 반사하지 못해 빔이 여기서 멈춘다. " +
                 "컴포넌트를 끄는 것과 다르다 — 꺼진 판은 빔에 보이지 않아 그냥 통과한다.")]
        public bool broken;

        /// <summary>고장이 풀린 순간. 연출·판정이 구독한다.</summary>
        public event System.Action<SolarReflector> Repaired;

        /// <summary>고장을 푼다. 이 프레임부터 다시 반사한다. 이미 멀쩡하면 아무 일도 없다.</summary>
        public void Repair()
        {
            if (!broken) return;

            broken = false;
            Repaired?.Invoke(this);
        }

        // 켜져 있는 거울들. 빔이 매 프레임 훑으므로 등록해 두고 쓴다.
        static readonly List<SolarReflector> active = new List<SolarReflector>();

        // 마지막으로 빛을 받은 프레임. IsLit 판정에만 쓴다.
        int litFrame = -1;

        /// <summary>이번 프레임에 빔이 닿았는가. (연출·판정에서 읽는다)</summary>
        public bool IsLit => litFrame == Time.frameCount;

        /// <summary>이번 프레임에 빔이 닿은 지점(월드). <see cref="IsLit"/>일 때만 유효.</summary>
        public Vector3 LitPoint { get; private set; }

        /// <summary>반사면 Transform. <see cref="surface"/>가 비면 자기 자신.</summary>
        public Transform Surface => surface != null ? surface : transform;

        /// <summary>반사면 중심(월드).</summary>
        public Vector3 Position => Surface.position;

        /// <summary>판 면의 법선(월드, 단위 벡터).</summary>
        public Vector3 Normal
        {
            get
            {
                Transform t = Surface;
                switch (normalAxis)
                {
                    case Axis.Down: return -t.up;
                    case Axis.Forward: return t.forward;
                    case Axis.Back: return -t.forward;
                    case Axis.Right: return t.right;
                    case Axis.Left: return -t.right;
                    default: return t.up;
                }
            }
        }

        // 사각형의 가로/세로 축. 법선과 직교하는 나머지 두 축을 쓴다.
        Vector3 Tangent
        {
            get
            {
                Transform t = Surface;
                switch (normalAxis)
                {
                    case Axis.Right:
                    case Axis.Left: return t.forward;
                    default: return t.right;
                }
            }
        }

        Vector3 Bitangent
        {
            get
            {
                Transform t = Surface;
                switch (normalAxis)
                {
                    case Axis.Up:
                    case Axis.Down: return t.forward;
                    default: return t.up;
                }
            }
        }

        void OnEnable() => active.Add(this);

        void OnDisable() => active.Remove(this);

        /// <summary>
        /// <paramref name="from"/>에서 <paramref name="dir"/>로 나간 빔이 맞는 거울 중 가장 가까운 것.
        /// 없으면 null.
        /// </summary>
        public static SolarReflector FindNearest(Vector3 from, Vector3 dir, float maxDistance,
                                                 SolarReflector ignore,
                                                 out float distance, out Vector3 point)
        {
            SolarReflector best = null;
            distance = maxDistance;
            point = default;

            for (int i = 0; i < active.Count; i++)
            {
                SolarReflector r = active[i];
                if (r == null || r == ignore) continue;

                // 지금까지 찾은 거리보다 먼 것은 볼 필요가 없다.
                if (!r.Raycast(from, dir, distance, out float d, out Vector3 p)) continue;

                best = r;
                distance = d;
                point = p;
            }

            return best;
        }

        /// <summary>빔이 이 거울의 사각형에 맞는가. 맞으면 거리와 지점을 준다.</summary>
        public bool Raycast(Vector3 from, Vector3 dir, float maxDistance, out float distance, out Vector3 point)
        {
            distance = 0f;
            point = default;

            Vector3 n = Normal;
            float denom = Vector3.Dot(dir, n);
            if (Mathf.Abs(denom) < 1e-5f) return false;   // 판과 나란한 빔

            float t = Vector3.Dot(Position - from, n) / denom;
            if (t < 1e-3f || t > maxDistance) return false;   // 뒤쪽이거나 사거리 밖

            Vector3 hit = from + dir * t;
            Vector3 local = hit - Position;
            if (Mathf.Abs(Vector3.Dot(local, Tangent)) > halfExtents.x) return false;
            if (Mathf.Abs(Vector3.Dot(local, Bitangent)) > halfExtents.y) return false;

            distance = t;
            point = hit;
            return true;
        }

        /// <summary>
        /// 들어온 방향을 반사 방향으로 바꾼다.
        /// 뒷면에 맞았거나(<see cref="twoSided"/>가 꺼진 경우) 너무 스치듯 맞으면 false —
        /// 그때 빔은 여기서 멈춘다.
        /// </summary>
        public bool TryReflect(Vector3 incoming, float minGain, out Vector3 outgoing, out float gain)
        {
            outgoing = incoming;
            gain = 0f;

            // 고장난 판은 빛을 받기만 하고 튕기지 못한다. 빔은 여기서 멈춘다.
            // 비활성과 다르다 — 비활성은 빔에 아예 보이지 않아 그냥 통과하고, IsLit도 켜지지 않는다.
            if (broken) return false;

            Vector3 n = Normal;
            gain = Vector3.Dot(n, -incoming);

            if (gain < 0f)
            {
                if (!twoSided) return false;
                n = -n;
                gain = -gain;
            }

            if (gain < minGain) return false;

            outgoing = Vector3.Reflect(incoming, n).normalized;
            return true;
        }

        /// <summary>이 콜라이더가 이 거울의 것인가. 자기 자신에 빔이 막히는 것을 피할 때 쓴다.</summary>
        public bool Owns(Collider other) => other != null && other.transform.IsChildOf(transform);

        /// <summary>빔이 닿았다고 표시한다. <see cref="SolarBeam"/>이 호출한다.</summary>
        public void MarkLit(Vector3 point)
        {
            litFrame = Time.frameCount;
            LitPoint = point;
        }

        void OnDrawGizmos()
        {
            Vector3 c = Position;
            Vector3 n = Normal;
            Vector3 u = Tangent * halfExtents.x;
            Vector3 v = Bitangent * halfExtents.y;

            Gizmos.color = IsLit ? new Color(1f, 0.95f, 0.4f, 0.9f) : new Color(0.4f, 0.8f, 1f, 0.45f);
            Gizmos.DrawLine(c - u - v, c + u - v);
            Gizmos.DrawLine(c + u - v, c + u + v);
            Gizmos.DrawLine(c + u + v, c - u + v);
            Gizmos.DrawLine(c - u + v, c - u - v);
            Gizmos.DrawLine(c, c + n * 0.5f);   // 어느 쪽이 앞면인지
        }
    }
}
