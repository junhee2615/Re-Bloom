using UnityEngine;
using UnityEngine.XR;

public class LivingRoot : MonoBehaviour
{
    [Header("Manager")]
    public RootMissionManager missionManager;

    [Header("Haptic")]
    [SerializeField] private float amplitude = 0.25f;   // 진동 세기
    [SerializeField] private float duration = 0.15f;    // 한 번 진동 길이
    [SerializeField] private float interval = 0.7f;     // 진동 주기 (느리게)

    public bool IsFound { get; private set; }

    private float timer;

private void OnTriggerStay(Collider other)
    {
        // 미션이 끝나 비활성화되면 진동/발견을 멈춘다.
        if (!enabled)
            return;

        // 오른손 Collider만 반응하도록 태그를 사용하는 것을 추천
        if (!other.CompareTag("Right Controller"))
            return;

        // ear 역할만 진동을 느낀다.
        if (!RoleManager.LocalIsEar)
            return;

        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            TriggerRightHandHaptic(amplitude, duration);
            timer = interval;
        }

        // 처음 접촉했을 때만 발견 처리
        if (!IsFound)
        {
            IsFound = true;
            missionManager?.OnRootFound(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Right Controller"))
        {
            timer = 0f;
        }
    }

    private void TriggerRightHandHaptic(float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities))
        {
            if (capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0, amplitude, duration);
            }
        }
    }
}
