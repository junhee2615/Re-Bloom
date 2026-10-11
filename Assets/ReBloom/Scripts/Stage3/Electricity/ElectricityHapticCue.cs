using UnityEngine;
using UnityEngine.XR;

namespace ReBloom.Electricity
{
    /// <summary>
    /// 전기 코어의 박자를 <b>ear에게만</b> 손 진동으로 들려준다.
    /// 빛은 보이지 않으므로, 이 진동이 ear가 가진 유일한 타이밍 정보다.
    ///
    /// 배치: <see cref="ElectricityPulseClock"/>과 같은 GameObject(또는 아무 데나 두고 참조를 채운다).
    /// </summary>
    /// <remarks>
    /// <b>세기만 키우면 "모이는 중"과 "맞음"이 구분되지 않는다</b>(`SolarBeamHaptics`에서 확인됐다).
    /// 그래서 박마다 한 번 때리되, 겹침이 가까워질수록 <b>세기와 길이를 함께</b> 올린다.
    /// 진동은 로컬 기기로만 나가므로 동기화하지 않는다 — 박 자체가 이미 모든 피어에서 같다.
    /// </remarks>
    [AddComponentMenu("ReBloom/Electricity Haptic Cue")]
    [DefaultExecutionOrder(300)]
    public class ElectricityHapticCue : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("박자를 읽어 올 시계. 비우면 같은 오브젝트에서 찾는다.")]
        public ElectricityPulseClock clock;

        [Header("역할")]
        [Tooltip("이 진동을 느낄 역할.")]
        public Role onlyForRole = Role.ear;

        [Tooltip("역할이 아직 정해지지 않았을 때도 울릴지. 단독 테스트용.")]
        public bool driveWhenRoleUnknown = true;

        public bool useLeftHand = true;
        public bool useRightHand = true;

        [Header("세기 — 겹침이 가까울수록 커진다")]
        [Tooltip("두 박이 멀 때의 세기.")]
        [Range(0f, 1f)] public float scatteredAmplitude = 0.25f;

        [Tooltip("겹친 순간의 세기.")]
        [Range(0f, 1f)] public float convergedAmplitude = 1f;

        [Tooltip("이보다 약하면 손에 느껴지지 않으므로 보내지 않는다.")]
        [Range(0f, 0.3f)] public float minPerceptible = 0.06f;

        [Header("길이 — 세기와 함께 길어져야 질감이 바뀐다")]
        [Range(0.02f, 0.3f)] public float scatteredDuration = 0.05f;
        [Range(0.02f, 0.4f)] public float convergedDuration = 0.18f;

        /// <summary>마지막으로 보낸 세기. 디버그·UI가 읽는다.</summary>
        public float LastAmplitude { get; private set; }

        bool wasOn;   // 직전 프레임에 박이 켜져 있었는가 — 박마다 한 번만 때리기 위해

        void Reset() => clock = GetComponent<ElectricityPulseClock>();

        void Awake()
        {
            if (clock == null) clock = GetComponent<ElectricityPulseClock>();
            if (clock == null) clock = FindFirstObjectByType<ElectricityPulseClock>();
        }

        /// <summary>이 피어가 진동을 받아야 하는가.</summary>
        public bool RoleMatches()
        {
            if (onlyForRole == Role.none) return true;
            if (!RoleManager.HasLocalRole) return driveWhenRoleUnknown;

            return RoleManager.IsLocalRole(onlyForRole);
        }

        void Update()
        {
            if (clock == null || !RoleMatches())
            {
                LastAmplitude = 0f;
                wasOn = false;
                return;
            }

            bool on = clock.BuzzOn;

            // 박이 켜지는 순간에만 한 번. 켜져 있는 내내 보내면 질감이 뭉개진다.
            if (on && !wasOn)
            {
                float t = clock.Convergence01;
                float amplitude = Mathf.Lerp(scatteredAmplitude, convergedAmplitude, t);
                float duration = Mathf.Lerp(scatteredDuration, convergedDuration, t);

                LastAmplitude = amplitude;

                if (amplitude >= minPerceptible)
                {
                    if (useRightHand) Send(XRNode.RightHand, amplitude, duration);
                    if (useLeftHand) Send(XRNode.LeftHand, amplitude, duration);
                }
            }

            wasOn = on;
        }

        static void Send(XRNode node, float amplitude, float duration)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0u, amplitude, Mathf.Max(0.01f, duration));
        }
    }
}
