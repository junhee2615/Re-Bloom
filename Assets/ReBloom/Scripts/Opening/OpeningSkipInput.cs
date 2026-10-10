using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Opening 스킵 입력 판정.
///
/// 왼손 Y 버튼을 <see cref="holdDuration"/> 초 이상 연속으로 누르면
/// <see cref="OpeningNarrationController.RequestSkip"/> 를 한 번 호출한다.
/// 실제 "안전하게 끝내는 순서"는 모두 OpeningNarrationController가 책임지고,
/// 이 스크립트는 입력만 본다.
///
/// 왜 Y(secondaryButton)인가:
/// 왼손 X(primaryButton)는 이미 UIPanel이 미션 패널 토글에 쓰고 있어 충돌한다.
/// 왼손 Y는 프로젝트 어디에서도 쓰지 않는다.
///
/// 왜 Time.unscaledDeltaTime인가:
/// 연출 중 timeScale이 바뀌어도 "3초 홀드"가 실제 3초로 유지되어야 한다.
///
/// 이 컴포넌트는 Opening 씬의 OpeningManager에 붙는다. 영속 XR Rig나 XR Origin
/// 프리팹은 건드리지 않으므로 Opening 씬이 사라지면 함께 사라진다.
/// </summary>
public class OpeningSkipInput : MonoBehaviour
{
    [Header("입력")]
    [Tooltip("왼손 Y 버튼을 이 시간 이상 연속으로 눌러야 스킵된다.")]
    [SerializeField, Min(0.1f)] private float holdDuration = 3f;

    [Header("References")]
    [Tooltip("비워 두면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private OpeningNarrationController narrationController;

    [Header("Debug")]
    [Tooltip("홀드가 시작/취소될 때 Console에 출력한다.")]
    [SerializeField] private bool logHoldState;

    private InputDevice leftController;
    private float heldTime;

    /// <summary>스킵을 이미 보냈는지. 한 번만 보낸다.</summary>
    private bool skipSent;

    /// <summary>현재 홀드 진행도 0~1. 안내 UI(<see cref="OpeningSkipUI"/>)가 읽는다.</summary>
    public float HoldProgress01 =>
        holdDuration <= 0f ? 0f : Mathf.Clamp01(heldTime / holdDuration);

    /// <summary>
    /// Y 버튼을 누르고 있는 중인지. 원형 게이지의 표시 여부를 정한다.
    ///
    /// 놓거나 장치가 무효해지면 <c>ResetHold</c>가 heldTime을 0으로 돌리므로
    /// 그 즉시 false가 된다. 별도의 입력 플래그를 두지 않는 이유다.
    /// </summary>
    public bool IsHolding => heldTime > 0f;

    private void Awake()
    {
        if (narrationController == null)
            narrationController = GetComponent<OpeningNarrationController>();

        if (narrationController == null)
        {
            Debug.LogWarning(
                "[Opening] OpeningNarrationController를 찾지 못해 스킵 입력을 받지 않습니다.", this);

            enabled = false;
        }
    }

    private void Update()
    {
        if (skipSent)
            return;

        // 이미 종료(정상 또는 스킵)가 시작됐으면 더 볼 것이 없다.
        if (narrationController.IsFinishing)
        {
            ResetHold("finishing");
            return;
        }

        // 장치가 일시적으로 무효해질 수 있다(슬립 / 재연결). 그때는 타이머를 초기화한다.
        if (!leftController.isValid)
        {
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

            if (!leftController.isValid)
            {
                ResetHold("device invalid");
                return;
            }
        }

        if (!leftController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed))
        {
            ResetHold("feature unavailable");
            return;
        }

        // 중간에 놓으면 0으로 돌아간다. 다시 누르면 처음부터 다시 센다.
        if (!pressed)
        {
            ResetHold("released");

            // 버튼을 놓으면 다시 요청할 수 있게 해제한다.
            // 스킵이 세션 요청 누락이나 씬 작업 타임아웃으로 취소된 경우에만 의미가 있다.
            // 정상적으로 스킵이 시작되면 위의 IsFinishing 검사에서 먼저 걸러지고,
            // RequestSkip 자체도 중복 호출을 무시하므로 재진입은 일어나지 않는다.
            skipSent = false;
            return;
        }

        if (heldTime <= 0f && logHoldState)
            Debug.Log("[Opening] Skip hold start", this);

        heldTime += Time.unscaledDeltaTime;

        if (heldTime < holdDuration)
            return;

        // 3초 도달 — 한 번만 실행한다.
        skipSent = true;
        heldTime = 0f;

        Debug.Log($"[Opening] Skip requested (left Y held {holdDuration}s)", this);

        narrationController.RequestSkip();
    }

    private void ResetHold(string reason)
    {
        if (heldTime <= 0f)
            return;

        if (logHoldState)
            Debug.Log($"[Opening] Skip hold reset ({reason})", this);

        heldTime = 0f;
    }
}
