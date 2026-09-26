using UnityEngine;
using UnityEngine.XR;

namespace ReBloom.Solar
{
    /// <summary>
    /// 조준 상태(<see cref="SolarBeamAim"/>)를 손의 진동으로 알려준다.
    /// <b>ear가 이 판 위에 올라서 있을 때만</b> 울린다 — 보이지 않는 것을 손으로 더듬어 맞추는 조작이다.
    ///
    /// 세 가지로 읽히게 만든다.
    ///  - 약하게 띄엄띄엄 → 빛이 퍼져 있다(위치가 다르다)
    ///  - 점점 강하고 촘촘해짐 → 빛이 모이는 중
    ///  - 끊기지 않는 강한 진동 → 정확한 위치(공명)
    ///
    /// <b>세기만 키우면 둘째와 셋째가 구분되지 않는다.</b> 그래서 맞기 전까지는 사이를 띄운 <b>펄스</b>로
    /// 보내고(가까울수록 간격이 좁아진다), 맞는 순간 간격을 길이보다 짧게 만들어 임펄스가 겹치는
    /// <b>끊기지 않는</b> 진동으로 질감을 바꾼다. 세기와 촘촘함이 함께 올라가야 "모이는 중"이 읽힌다.
    ///
    /// 진동은 로컬 기기로만 나가므로 동기화하지 않는다.
    /// 배치: <see cref="SolarBeamAim"/>과 같은 GameObject(= 빔을 쏘는 판).
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Beam Haptics")]
    [RequireComponent(typeof(SolarBeamAim))]
    [DefaultExecutionOrder(300)]   // SolarBeamAim(200)이 값을 갱신한 뒤에 읽는다.
    public class SolarBeamHaptics : MonoBehaviour
    {
        [Header("누가 느끼나")]
        [Tooltip("이 역할에게만 진동을 보낸다. none이면 역할을 따지지 않는다.")]
        public Role onlyForRole = Role.ear;

        [Tooltip("이 판을 밟고 있을 때만 울린다. 조준하는 본인만 느껴야 하므로 기본은 켬.")]
        public bool requireStandingOnPanel = true;

        [Tooltip("세션 밖이라 역할을 알 수 없을 때도 보낼지. 에디터 단독 테스트용.")]
        public bool driveWhenRoleUnknown = true;

        [Header("손")]
        public bool useLeftHand = true;
        public bool useRightHand = true;

        [Header("세기")]
        [Tooltip("완전히 빗나갔을 때의 세기. '아직 뭔가 있다'만 알려줄 정도로 약하게.")]
        [Range(0f, 1f)] public float scatteredAmplitude = 0.1f;

        [Tooltip("표적에 거의 닿기 직전의 세기. 아래 onTarget보다 확실히 낮아야 도착이 구분된다.")]
        [Range(0f, 1f)] public float convergingAmplitude = 0.45f;

        [Tooltip("정확히 맞았을 때의 세기.")]
        [Range(0f, 1f)] public float onTargetAmplitude = 1f;

        [Tooltip("이 세기 미만은 컨트롤러가 사실상 못 느끼므로 보내지 않는다.")]
        [Range(0f, 0.3f)] public float minPerceptible = 0.06f;

        [Header("리듬")]
        [Tooltip("완전히 빗나갔을 때의 펄스 간격(초). 길수록 '멀다'로 읽힌다.")]
        [Range(0.05f, 1f)] public float scatteredInterval = 0.4f;

        [Tooltip("표적에 거의 닿기 직전의 펄스 간격(초).")]
        [Range(0.03f, 0.5f)] public float convergingInterval = 0.1f;

        [Tooltip("한 번 보내는 임펄스 길이(초). 맞았을 때는 이보다 짧은 간격으로 보내 사이를 없앤다.")]
        [Range(0.02f, 0.3f)] public float pulseDuration = 0.07f;

        /// <summary>지금 실제로 보내고 있는 세기. 디버그 표시용.</summary>
        public float LastAmplitude { get; private set; }

        SolarBeamAim aim;
        SolarPanelTilt tilt;
        float nextPulseTime;

        void Awake()
        {
            aim = GetComponent<SolarBeamAim>();
            tilt = GetComponent<SolarPanelTilt>();

            if (requireStandingOnPanel && tilt == null)
                Debug.LogWarning($"[SolarBeamHaptics] {name}: SolarPanelTilt가 없어 올라섰는지 알 수 없습니다. " +
                                 $"requireStandingOnPanel을 끄거나 같은 오브젝트에 SolarPanelTilt를 붙이세요.", this);
        }

        void Update()
        {
            if (!ShouldDrive())
            {
                LastAmplitude = 0f;
                return;
            }

            float proximity = Mathf.Clamp01(aim.Proximity);
            bool onTarget = aim.State == SolarBeamAim.AimState.OnTarget;

            float amplitude = onTarget
                ? onTargetAmplitude
                : Mathf.Lerp(scatteredAmplitude, convergingAmplitude, proximity);

            LastAmplitude = amplitude;

            // 맞았으면 임펄스가 겹치도록 길이보다 짧은 간격으로 보낸다 — 사이가 벌어지지 않아야
            // "끊기지 않는 공명"으로 느껴진다.
            float interval = onTarget
                ? pulseDuration * 0.8f
                : Mathf.Lerp(scatteredInterval, convergingInterval, proximity);

            if (Time.unscaledTime < nextPulseTime) return;
            nextPulseTime = Time.unscaledTime + Mathf.Max(0.01f, interval);

            if (amplitude < minPerceptible) return;

            if (useRightHand) Send(XRNode.RightHand, amplitude);
            if (useLeftHand) Send(XRNode.LeftHand, amplitude);
        }

        // 빔이 꺼졌거나(살릴 판이 없거나), 내 역할이 아니거나, 판에서 내려왔으면 울리지 않는다.
        bool ShouldDrive()
        {
            if (aim == null || aim.State == SolarBeamAim.AimState.Off) return false;
            if (!RoleMatches()) return false;
            if (requireStandingOnPanel && (tilt == null || !tilt.IsOccupied)) return false;

            return true;
        }

        /// <summary>로컬 역할이 대상인가. 역할 미확정이면 <see cref="driveWhenRoleUnknown"/>을 따른다.</summary>
        public bool RoleMatches()
        {
            if (onlyForRole == Role.none) return true;
            if (!RoleManager.HasLocalRole) return driveWhenRoleUnknown;

            return RoleManager.IsLocalRole(onlyForRole);
        }

        void Send(XRNode node, float amplitude)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0u, amplitude, Mathf.Max(0.01f, pulseDuration));
        }
    }
}
