using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 빔을 한 번 더 튕겨 보내는 거울면. 씬에 배치된 태양광 패널에 붙인다.
    ///
    /// 거울인지는 <b>콜라이더</b>로 판정한다
    /// — <see cref="SolarBeam"/>의 물리 레이가 맞은 콜라이더의 부모에 이 컴포넌트가 있으면 거울이다.
    /// 단, 맞은 면이 판 <b>앞면</b>일 때만이다(<see cref="IsFrontFace"/>).
    ///
    /// 반사에 쓰는 법선은 콜라이더가 아니라 <see cref="Normal"/>(이 Transform의 up)이라 모든 피어가 같은 결과를 얻는다 — 동기화하지 않는다.
    ///
    /// 배치: 판 면 오브젝트(콜라이더가 있는 것)에 붙인다. 이 Transform의 <b>up이 곧 판 법선</b>이다.
    /// 컴포넌트를 끄면 빔에 거울로 보이지 않는다 — 콜라이더는 남아 있으므로 빔은 거기서 <b>막힌다</b>.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Reflector")]
    public class SolarReflector : MonoBehaviour
    {
        [Tooltip("맞은 콜라이더 면이 판 앞면으로 인정되는 최대 각도(도). " +
                 "콜라이더의 옆면·뒷면은 이 각도를 넘으므로 거울이 아니라 벽으로 취급된다.")]
        [Range(0f, 45f)] public float faceAngleTolerance = 15f;

        [Header("고장")]
        [Tooltip("고장난 상태. 빛은 받지만(IsLit) 반사하지 못해 빔이 여기서 멈춘다.")]
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

        // 마지막으로 빛을 받은 프레임. IsLit 판정에만 쓴다.
        int litFrame = -1;

        /// <summary>이번 프레임에 빔이 닿았는가. (연출·판정에서 읽는다)</summary>
        public bool IsLit => litFrame == Time.frameCount;

        /// <summary>이번 프레임에 빔이 닿은 지점(월드). <see cref="IsLit"/>일 때만 유효.</summary>
        public Vector3 LitPoint { get; private set; }

        /// <summary>반사면 중심(월드).</summary>
        public Vector3 Position => transform.position;

        /// <summary>판 면의 법선(월드, 단위 벡터). 이 Transform의 up이다.</summary>
        public Vector3 Normal => transform.up;

        /// <summary>
        /// 이 콜라이더를 가진 활성 거울을 찾는다. 없거나 꺼져 있으면 false — 그러면 빔은 그 콜라이더에 벽처럼 막힌다.
        /// </summary>
        public static bool TryGet(Collider collider, out SolarReflector mirror)
        {
            mirror = null;
            if (collider == null) return false;

            SolarReflector r = collider.GetComponentInParent<SolarReflector>();
            if (r == null || !r.isActiveAndEnabled) return false;

            mirror = r;
            return true;
        }

        /// <summary>
        /// 레이가 맞은 면이 판 앞면인가. — 맞은 면의 법선이 판 법선과 <see cref="faceAngleTolerance"/> 안이어야 한다.
        /// </summary>
        public bool IsFrontFace(Vector3 hitNormal)
        {
            return Vector3.Dot(hitNormal, Normal) >= Mathf.Cos(faceAngleTolerance * Mathf.Deg2Rad);
        }

        /// <summary>
        /// 들어온 방향을 반사 방향으로 바꾼다. 고장이거나 너무 스치듯 맞으면 false — 그때 빔은 여기서 멈춘다.
        /// </summary>
        public bool TryReflect(Vector3 incoming, float minGain, out Vector3 outgoing)
        {
            outgoing = incoming;

            // 고장난 판은 빛을 받기만 하고 튕기지 못한다. 빔은 여기서 멈춘다.
            if (broken) return false;

            Vector3 n = Normal;
            float gain = Vector3.Dot(n, -incoming);   // 얼마나 정면으로 맞았나. 스치듯 맞으면 반사하지 않는다.
            if (gain < minGain) return false;

            outgoing = Vector3.Reflect(incoming, n).normalized;
            return true;
        }

        /// <summary>빔이 닿았다고 표시한다. <see cref="SolarBeam"/>이 호출한다.</summary>
        public void MarkLit(Vector3 point)
        {
            litFrame = Time.frameCount;
            LitPoint = point;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsLit ? new Color(1f, 0.95f, 0.4f, 0.9f) : new Color(0.4f, 0.8f, 1f, 0.45f);
            Gizmos.DrawLine(Position, Position + Normal * 0.5f);   // 어느 쪽이 앞면인지
        }
    }
}
