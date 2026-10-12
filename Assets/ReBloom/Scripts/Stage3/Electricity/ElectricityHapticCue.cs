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
    /// 그래서 박마다 한 번 때리되, 가속 중에는 <b>세기와 길이를 함께</b> 올린다.
    ///
    /// <b>⚠ 그렇다고 응집을 그 보간의 끝점으로 두면 안 된다.</b> 연속값(<c>Convergence01</c>)으로
    /// 가르면 램프의 마지막 박이 이미 0.9를 넘어서, 응집과의 차이가 세기·길이 모두 <b>6%</b>밖에
    /// 안 된다 — 손으로는 같은 박이다(2026-10-12 실측). 그래서 둘을 나눴다:
    /// 램프는 <see cref="rampPeakAmplitude"/>까지만 올라가는 <b>짧은 탭</b>이고,
    /// 응집은 <see cref="clock"/>의 <c>IsConvergeBeatOn</c>으로 갈라낸 <b>길고 센 울림</b>이다.
    /// 빛이 유지 시간(0.08 → 0.4초)으로 질적 차이를 만드는 것과 같은 역할을 진동에서 한다.
    ///
    /// 진동은 로컬 기기로만 나가므로 동기화하지 않는다 — 박 자체가 이미 모든 피어에서 같다.
    /// </remarks>
    [AddComponentMenu("ReBloom/Electricity Haptic Cue")]
    [DefaultExecutionOrder(300)]
    public class ElectricityHapticCue : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("박자를 읽어 올 시계. 비우면 같은 오브젝트에서 찾는다.")]
        public ElectricityPulseClock clock;

        [Tooltip("레버 점유를 읽어 올 미션. 비우면 같은 오브젝트에서 찾는다.")]
        public ElectricityCoreMission mission;

        [Header("역할")]
        [Tooltip("이 진동을 느낄 역할.")]
        public Role onlyForRole = Role.ear;

        [Tooltip("역할이 아직 정해지지 않았을 때도 울릴지. 단독 테스트용.")]
        public bool driveWhenRoleUnknown = true;

        public bool useLeftHand = true;
        public bool useRightHand = true;

        [Header("가속 램프 — 짧은 탭, 뒤로 갈수록 세진다")]
        [Tooltip("램프 첫 박의 세기.")]
        [Range(0f, 1f)] public float scatteredAmplitude = 0.2f;

        [Tooltip("램프 **마지막** 박의 세기. 응집보다 충분히 낮아야 한다 — " +
                 "여기가 1에 가까우면 응집에서 더 올릴 여지가 없어 두 박이 같게 느껴진다.")]
        [Range(0f, 1f)] public float rampPeakAmplitude = 0.55f;

        [Tooltip("램프 첫 박의 길이(초).")]
        [Range(0.02f, 0.3f)] public float scatteredDuration = 0.05f;

        [Tooltip("램프 마지막 박의 길이(초). 램프는 '탭'으로 남아야 응집의 '울림'과 갈린다.")]
        [Range(0.02f, 0.3f)] public float rampPeakDuration = 0.09f;

        [Header("응집 — 질적으로 다른 한 방")]
        [Tooltip("응집 박의 세기. 램프 상한과의 **차이**가 '여기다'를 만든다.")]
        [Range(0f, 1f)] public float convergedAmplitude = 1f;

        [Tooltip("응집 박의 길이(초). 빛의 convergeHoldDuration과 맞춰 두면 두 감각이 같은 사건을 가리킨다. " +
                 "램프 탭의 3~4배는 돼야 '탭'이 아니라 '울림'으로 읽힌다.")]
        [Range(0.05f, 0.6f)] public float convergedDuration = 0.35f;

        [Header("공통")]
        [Tooltip("이보다 약하면 손에 느껴지지 않으므로 보내지 않는다.")]
        [Range(0f, 0.3f)] public float minPerceptible = 0.06f;

        /// <summary>마지막으로 보낸 세기. 디버그·UI가 읽는다.</summary>
        public float LastAmplitude { get; private set; }

        bool wasOn;   // 직전 프레임에 박이 켜져 있었는가 — 박마다 한 번만 때리기 위해

        void Reset() => clock = GetComponent<ElectricityPulseClock>();

        void Awake()
        {
            if (clock == null) clock = GetComponent<ElectricityPulseClock>();
            if (clock == null) clock = FindFirstObjectByType<ElectricityPulseClock>();

            if (mission == null) mission = GetComponent<ElectricityCoreMission>();
            if (mission == null) mission = FindFirstObjectByType<ElectricityCoreMission>();
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
            // 자기 레버를 잡고 있을 때만 울린다 — 박자는 공유하되 신호는 각자다.
            bool holding = mission == null || mission.IsHeldBy(onlyForRole);

            if (clock == null || !RoleMatches() || !holding)
            {
                LastAmplitude = 0f;
                wasOn = false;
                return;
            }

            bool on = clock.BuzzOn;

            // 박이 켜지는 순간에만 한 번. 켜져 있는 내내 보내면 질감이 뭉개진다.
            if (on && !wasOn)
            {
                // 응집은 램프의 연장이 아니라 **다른 사건**이다. 연속값으로 가르려 하면
                // 램프 마지막 박이 이미 0.9를 넘어 있어 차이가 손에 안 느껴진다(실측 6%).
                // 램프는 자기 상한까지만 올라가고, 응집은 거기서 뚝 끊어 올린다.
                bool converge = clock.IsConvergeBeatOn;
                float t = clock.RampProgress01;

                float amplitude = converge
                    ? convergedAmplitude
                    : Mathf.Lerp(scatteredAmplitude, rampPeakAmplitude, t);

                float duration = converge
                    ? convergedDuration
                    : Mathf.Lerp(scatteredDuration, rampPeakDuration, t);

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
