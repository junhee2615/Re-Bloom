using UnityEngine;
using UnityEngine.XR;

namespace ReBloom.Water
{
    /// <summary>
    /// ear 캐릭터 전용 연속 진동.
    /// RiverbedFlowController.CurrentHapticIntensity 를 매 프레임 읽어
    /// 짧은 임펄스를 겹쳐 보내는 방식으로 "끊기지 않는 세기"를 만든다.
    ///
    /// 세기 곡선(평탄 구간 포함)은 컨트롤러의 Haptic Falloff 에서 만진다.
    /// </summary>
    [AddComponentMenu("ReBloom/Riverbed Ear Haptics")]
    public class RiverbedEarHaptics : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 자동으로 찾는다")]
        public RiverbedFlowController controller;

        [Header("역할")]
        [Tooltip("이 역할에게만 진동을 보낸다. mental 은 진동을 느끼면 안 된다")]
        public Role onlyForRole = Role.ear;

        [Tooltip("none 이면 NetworkPlayer 에서 로컬 역할을 읽는다. 단독 테스트할 때만 강제 지정")]
        public Role roleOverride = Role.none;

        [Tooltip("세션 밖이라 역할을 알 수 없을 때도 진동을 보낼지 (에디터 단독 테스트용)")]
        public bool driveWhenRoleUnknown = true;

        [Header("손")]
        public bool useLeftHand = true;
        public bool useRightHand = true;

        [Header("임펄스")]
        [Tooltip("재전송 간격 (초). 짧을수록 매끄럽다")]
        [Range(0.02f, 0.2f)] public float pulseInterval = 0.05f;

        [Tooltip("한 번 보내는 임펄스 길이. 간격보다 조금 길어야 사이가 안 벌어진다")]
        [Range(0.02f, 0.3f)] public float pulseDuration = 0.07f;

        [Tooltip("이 세기 미만은 컨트롤러가 사실상 못 느끼므로 보내지 않는다")]
        [Range(0f, 0.3f)] public float minPerceptible = 0.06f;

        float nextPulseTime;

        /// <summary>지금 실제로 보내고 있는 세기. 디버그 표시용.</summary>
        public float LastAmplitude { get; private set; }

        void Reset()
        {
            controller = Object.FindFirstObjectByType<RiverbedFlowController>();
        }

        void Awake()
        {
            if (controller == null)
                controller = Object.FindFirstObjectByType<RiverbedFlowController>();
        }

        public bool RoleMatches()
        {
            if (onlyForRole == Role.none) return true;

            if (roleOverride != Role.none) return roleOverride == onlyForRole;

            Role? local = NetworkPlayer.LocalRole;
            if (!local.HasValue) return driveWhenRoleUnknown;
            return local.Value == onlyForRole;
        }

        void Update()
        {
            if (controller == null) return;

            if (!RoleMatches())
            {
                LastAmplitude = 0f;
                return;
            }

            if (Time.unscaledTime < nextPulseTime) return;
            nextPulseTime = Time.unscaledTime + Mathf.Max(0.01f, pulseInterval);

            float amplitude = Mathf.Clamp01(controller.CurrentHapticIntensity);
            LastAmplitude = amplitude;

            if (amplitude < minPerceptible) return;

            if (useRightHand) Send(XRNode.RightHand, amplitude);
            if (useLeftHand) Send(XRNode.LeftHand, amplitude);
        }

        void Send(XRNode node, float amplitude)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            HapticCapabilities caps;
            if (device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0u, amplitude, Mathf.Max(0.01f, pulseDuration));
        }
    }
}
