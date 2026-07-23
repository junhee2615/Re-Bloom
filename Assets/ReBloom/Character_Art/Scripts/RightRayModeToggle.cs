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
    private bool previousAButtonState;
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

        if (!rightController.TryGetFeatureValue(
                CommonUsages.primaryButton,
                out bool currentAButtonState))
        {
            return;
        }

        // A 버튼을 처음 누른 순간에만 한 번 토글
        if (currentAButtonState && !previousAButtonState)
        {
            isUIMode = !isUIMode;
            ApplyMode();
        }

        previousAButtonState = currentAButtonState;
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
        if (teleportInteractor != null)
            teleportInteractor.SetActive(!isUIMode);

        if (uiInteractor != null)
            uiInteractor.SetActive(isUIMode);
    }
}