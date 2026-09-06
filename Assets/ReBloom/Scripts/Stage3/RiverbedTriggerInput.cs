using UnityEngine;
using UnityEngine.XR;

namespace ReBloom.Water
{
    /// <summary>
    /// Controller trigger -> RiverbedFlowController.SubmitInput(role).
    /// Both mental and ear press the trigger; the controller applies a different
    /// judgment rule per role.
    ///
    /// Put ONE of these in the scene. It reads the local headset's devices and the
    /// local player's role, so each machine judges with its own player's rule.
    /// </summary>
    [AddComponentMenu("ReBloom/Riverbed Trigger Input")]
    public class RiverbedTriggerInput : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 자동으로 찾는다")]
        public RiverbedFlowController controller;

        [Header("입력")]
        public bool useLeftHand = true;
        public bool useRightHand = true;

        [Tooltip("아날로그 트리거가 이 값을 넘으면 눌린 것으로 본다")]
        [Range(0.1f, 1f)] public float pressThreshold = 0.6f;

        [Header("역할")]
        [Tooltip("none 이면 NetworkPlayer 에서 로컬 역할을 읽는다. 단독 테스트할 때만 강제 지정")]
        public Role roleOverride = Role.none;

        [Tooltip("이 역할일 때만 입력을 받는다. none 이면 제한 없음")]
        public Role onlyForRole = Role.none;

        [Header("디버그")]
        public bool logToConsole = true;

        bool prevPressed;

        void Reset()
        {
            controller = Object.FindFirstObjectByType<RiverbedFlowController>();
        }

        void Awake()
        {
            if (controller == null)
                controller = Object.FindFirstObjectByType<RiverbedFlowController>();
        }

        /// <summary>이 기기의 로컬 역할. 세션 밖(에디터 단독)이면 Role.none.</summary>
        public Role ResolveRole()
        {
            if (roleOverride != Role.none) return roleOverride;
            Role? r = NetworkPlayer.LocalRole;
            return r.HasValue ? r.Value : Role.none;
        }

        void Update()
        {
            if (controller == null) return;

            bool pressed = false;
            if (useRightHand) pressed |= IsTriggerPressed(XRNode.RightHand);
            if (useLeftHand) pressed |= IsTriggerPressed(XRNode.LeftHand);

            if (pressed && !prevPressed)
            {
                Role role = ResolveRole();
                if (onlyForRole == Role.none || role == onlyForRole)
                {
                    controller.SubmitInput(role);
                    if (logToConsole)
                        Debug.Log("[RiverbedTriggerInput] trigger submit as role=" + role, this);
                }
            }

            prevPressed = pressed;
        }

        bool IsTriggerPressed(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return false;

            bool button;
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out button) && button)
                return true;

            float axis;
            if (device.TryGetFeatureValue(CommonUsages.trigger, out axis) && axis >= pressThreshold)
                return true;

            return false;
        }
    }
}
