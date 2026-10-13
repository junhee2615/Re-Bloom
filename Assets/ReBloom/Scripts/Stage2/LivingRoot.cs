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

    [Header("발견 표시")]
    [Tooltip("뿌리를 찾았을 때 밑에서 퍼지는 초록 동심원 이펙트(FoundCircle). 두 플레이어 모두에게 보인다.")]
    [SerializeField] private GameObject foundEffect;

    public bool IsFound { get; private set; }

    private float timer;
    // 발견을 이미 매니저에 보고했는가 (네트워크 응답이 오기 전 중복 보고 방지)
    private bool reported;

    private void Awake()
    {
        if (foundEffect != null)
            foundEffect.SetActive(false);

        if (missionManager == null)
            missionManager = FindFirstObjectByType<RootMissionManager>();
    }

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

        // 처음 접촉했을 때만 발견 보고. ear 가 손을 대고 진동을 느낀 경우에만 여기까지 온다.
        // 실제 발견 처리(이펙트 포함)는 매니저가 두 플레이어 모두에게 적용한다.
        if (!IsFound && !reported)
        {
            reported = true;
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

    /// <summary>발견 처리. RootMissionManager 가 모든 플레이어에서 호출한다.</summary>
    public void MarkFound()
    {
        if (IsFound)
            return;

        IsFound = true;
        reported = true;
        SetFoundEffect(true);
    }

    /// <summary>찾기 미션이 끝나면 발견 표시를 끈다.</summary>
    public void HideFoundEffect()
    {
        SetFoundEffect(false);
    }

    private void SetFoundEffect(bool visible)
    {
        if (foundEffect == null)
            return;

        foundEffect.SetActive(visible);

        if (visible)
        {
            ParticleSystem ps = foundEffect.GetComponent<ParticleSystem>();
            if (ps != null)
                ps.Play(true);
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
