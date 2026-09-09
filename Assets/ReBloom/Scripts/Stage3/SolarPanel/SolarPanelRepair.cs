using UnityEngine;
using UnityEngine.Events;

namespace ReBloom.Solar
{
    /// <summary>
    /// 고장난 반사판에 빛이 머무르면 살아난다.
    ///
    /// 살아난 판은 그 프레임부터 스스로 빛을 튕기므로, 체인이 한 칸씩 저절로 이어진다.
    /// 고장 여부는 <see cref="SolarReflector.broken"/>이 들고 있고 이 컴포넌트는 <b>시간만</b> 센다.
    ///
    /// 배치: <see cref="SolarReflector"/>와 같은 GameObject에 붙인다.
    /// </summary>
    /// <remarks>
    /// <see cref="SolarReflector.IsLit"/>은 "이번 프레임에 빔이 닿았는가"라서
    /// <see cref="SolarBeam"/>이 <c>LateUpdate</c>에서 표시한 <b>뒤에</b> 읽어야 한다.
    /// Update에서 읽으면 항상 false다(한 프레임 늦게 보게 된다).
    /// 그래서 이 컴포넌트도 LateUpdate를 쓰고, 실행 순서를 뒤로 못 박았다.
    /// </remarks>
    [AddComponentMenu("ReBloom/Solar Panel Repair")]
    [RequireComponent(typeof(SolarReflector))]
    [DefaultExecutionOrder(200)]   // SolarBeam(기본 0)의 LateUpdate보다 뒤
    public class SolarPanelRepair : MonoBehaviour
    {
        [Tooltip("빛을 이만큼(초) 받으면 살아난다. 0이면 닿는 즉시.")]
        [Min(0f)] public float litSecondsToRepair = 1.5f;

        [Tooltip("빛이 끊겼을 때 진행도가 되돌아가는 속도 배수. 0이면 진행도를 그대로 유지한다.")]
        [Min(0f)] public float decayMultiplier = 1f;

        [Tooltip("살아난 순간 한 번 호출된다.")]
        public UnityEvent onRepaired;

        SolarReflector reflector;
        float lit;

        /// <summary>수리 진행도 0~1. 연출이 읽는다.</summary>
        public float Progress =>
            litSecondsToRepair <= 0f ? (reflector != null && reflector.broken ? 0f : 1f)
                                     : Mathf.Clamp01(lit / litSecondsToRepair);

        /// <summary>아직 고장난 상태인가.</summary>
        public bool IsBroken => reflector != null && reflector.broken;

        void Awake()
        {
            reflector = GetComponent<SolarReflector>();
        }

        void LateUpdate()
        {
            if (reflector == null || !reflector.broken) return;

            if (reflector.IsLit)
                lit += Time.deltaTime;
            else if (decayMultiplier > 0f)
                lit = Mathf.Max(0f, lit - Time.deltaTime * decayMultiplier);

            if (lit < litSecondsToRepair) return;

            reflector.Repair();
            onRepaired?.Invoke();
        }

        /// <summary>다시 고장 상태로 되돌린다. 미션 재시도에 쓴다.</summary>
        public void ResetToBroken()
        {
            lit = 0f;

            if (reflector != null)
                reflector.broken = true;
        }
    }
}
