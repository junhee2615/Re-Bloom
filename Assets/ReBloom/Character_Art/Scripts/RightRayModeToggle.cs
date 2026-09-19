using UnityEngine;
using UnityEngine.XR;

public class RightRayModeToggle : MonoBehaviour
{
    /// <summary>Persistent XR Rig에 하나만 존재한다. 다른 씬의 스크립트가 참조할 때 사용.</summary>
    public static RightRayModeToggle Instance { get; private set; }

    [Header("오른쪽 레이 오브젝트")]
    [SerializeField] private GameObject teleportInteractor;
    [SerializeField] private GameObject uiInteractor;

    [Header("시작 모드")]
    [SerializeField] private bool startWithUIMode = false;

    private InputDevice rightController;

    // A 버튼 상태 → B 버튼 상태로 변경
    private bool previousBButtonState;

    // 현재 눌려 있는 B 입력이 다른 기능(개발용 Hold 등)에 소비되었는지.
    // 소비된 입력은 놓을 때 Ray Mode를 토글하지 않는다.
    private bool currentBPressConsumed;

    private bool isUIMode;

    /// <summary>현재 UI Ray 모드인지.</summary>
    public bool IsUIMode => isUIMode;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        FindRightController();

        isUIMode = startWithUIMode;
        ApplyMode();
    }

    private void Update()
    {
        if (!rightController.isValid)
        {
            FindRightController();
            return;
        }

        // primaryButton(A) → secondaryButton(B)
        if (!rightController.TryGetFeatureValue(
                CommonUsages.secondaryButton,
                out bool currentBButtonState))
        {
            return;
        }

        // B 버튼을 누르기 시작: 상태만 기록하고 아직 토글하지 않는다.
        if (currentBButtonState && !previousBButtonState)
        {
            currentBPressConsumed = false;
        }

        // B 버튼을 놓을 때: 다른 기능이 소비하지 않은 입력(짧게 누르기)만 토글한다.
        if (!currentBButtonState && previousBButtonState)
        {
            if (!currentBPressConsumed)
                ToggleMode();

            currentBPressConsumed = false;
        }

        previousBButtonState = currentBButtonState;
    }

    /// <summary>
    /// 지금 눌려 있는 B 입력을 다른 기능이 사용했다고 표시한다.
    /// 이후 버튼을 놓아도 Ray Mode는 토글되지 않는다. (다음 누름부터 다시 정상 동작)
    /// </summary>
    public void ConsumeCurrentBPress()
    {
        currentBPressConsumed = true;
    }

    /// <summary>Teleport 모드일 때만 UI Ray 모드로 바꾼다. 이미 UI 모드면 아무것도 하지 않는다.</summary>
    public void EnsureUIRayMode()
    {
        if (isUIMode)
            return;

        isUIMode = true;
        ApplyMode();
    }

    private void ToggleMode()
    {
        isUIMode = !isUIMode;
        ApplyMode();
    }

    private void FindRightController()
    {
        rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!rightController.isValid)
        {
            Debug.LogWarning(
                "[RightRayModeToggle] 오른쪽 XR 컨트롤러를 찾지 못했습니다.",
                this
            );
        }
    }

    private void ApplyMode()
    {
        if (isUIMode)
        {
            // UI 모드
            if (teleportInteractor != null)
                teleportInteractor.SetActive(false);

            if (uiInteractor != null)
                uiInteractor.SetActive(true);
        }
        else
        {
            // 텔레포트 대기 모드
            if (uiInteractor != null)
                uiInteractor.SetActive(false);

            // 바로 표시하지 않고 비활성 상태로 대기
            if (teleportInteractor != null)
                teleportInteractor.SetActive(false);
        }
    }
}
