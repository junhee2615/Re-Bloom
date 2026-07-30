using UnityEngine;
using UnityEngine.XR;

public class RightRayModeToggle : MonoBehaviour
{
    [Header("오른쪽 레이 오브젝트")]
    [SerializeField] private GameObject teleportInteractor;
    [SerializeField] private GameObject uiInteractor;

    [Header("시작 모드")]
    [SerializeField] private bool startWithUIMode = false;

    private InputDevice rightController;

    // A 버튼 상태 → B 버튼 상태로 변경
    private bool previousBButtonState;

    private bool isUIMode;

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

        // B 버튼을 처음 누른 순간에만 한 번 토글
        if (currentBButtonState && !previousBButtonState)
        {
            isUIMode = !isUIMode;
            ApplyMode();
        }

        previousBButtonState = currentBButtonState;
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