using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(300)]
public class LocomotionHeightLock : MonoBehaviour
{
    [Header("기능 ON/OFF")]
    [Tooltip("체크하면 직접 이동(Smooth Locomotion) 중 카메라 Y축 높이를 고정합니다.")]
    [SerializeField] private bool enableHeightLock = false;

    [Header("전환 설정")]
    [Tooltip("조이스틱을 놓았을 때 원래 높이로 복귀하는 속도 (높을수록 빠름)")]
    [SerializeField] private float returnSpeed = 5f;

    [Tooltip("왼쪽 조이스틱 입력 감지 임계값")]
    [SerializeField] private float inputThreshold = 0.1f;

    [Header("참조 (비워두면 자동 탐색)")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform cameraOffsetTransform;
    [SerializeField] private Transform mainCameraTransform;

    [Header("디버그 (읽기 전용)")]
    [SerializeField] private bool isLocomotionActive;
    [SerializeField] private float lockedHmdLocalY;
    [SerializeField] private float currentCompensation;

    private bool wasLocomotionActive;
    private bool isCompensating;
    private float savedOffsetY;

    public bool EnableHeightLock
    {
        get => enableHeightLock;
        set => enableHeightLock = value;
    }

    private void OnEnable()
    {
        FindReferences();
    }

    private void FindReferences()
    {
        if (xrOrigin == null)
            xrOrigin = GetComponentInParent<XROrigin>();
        if (xrOrigin == null)
            xrOrigin = GetComponent<XROrigin>();
        if (xrOrigin == null)
            return;

        if (cameraOffsetTransform == null)
        {
            GameObject offsetObj = xrOrigin.CameraFloorOffsetObject;
            if (offsetObj != null)
                cameraOffsetTransform = offsetObj.transform;
        }

        if (mainCameraTransform == null && xrOrigin.Camera != null)
            mainCameraTransform = xrOrigin.Camera.transform;
    }

    private void LateUpdate()
    {
        if (cameraOffsetTransform == null || mainCameraTransform == null || xrOrigin == null)
            return;

        float hmdLocalY = mainCameraTransform.localPosition.y;
        bool joystickActive = ReadLeftJoystick();
        isLocomotionActive = joystickActive;

        if (enableHeightLock && joystickActive)
        {
            if (!wasLocomotionActive)
            {
                savedOffsetY = cameraOffsetTransform.localPosition.y;
                lockedHmdLocalY = hmdLocalY;
                wasLocomotionActive = true;
            }

            currentCompensation = lockedHmdLocalY - hmdLocalY;
            isCompensating = true;
        }
        else
        {
            wasLocomotionActive = false;

            if (isCompensating)
            {
                currentCompensation = Mathf.Lerp(
                    currentCompensation, 0f, Time.deltaTime * returnSpeed);

                if (Mathf.Abs(currentCompensation) < 0.0005f)
                {
                    currentCompensation = 0f;
                    isCompensating = false;

                    Vector3 pos = cameraOffsetTransform.localPosition;
                    pos.y = savedOffsetY;
                    cameraOffsetTransform.localPosition = pos;
                    return;
                }
            }
        }

        if (isCompensating)
        {
            Vector3 pos = cameraOffsetTransform.localPosition;
            pos.y = savedOffsetY + currentCompensation;
            cameraOffsetTransform.localPosition = pos;
        }
    }

    private bool ReadLeftJoystick()
    {
        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            return axis.magnitude > inputThreshold;
        return false;
    }

    private void OnDisable()
    {
        if (isCompensating && cameraOffsetTransform != null)
        {
            Vector3 pos = cameraOffsetTransform.localPosition;
            pos.y = savedOffsetY;
            cameraOffsetTransform.localPosition = pos;
        }

        currentCompensation = 0f;
        wasLocomotionActive = false;
        isCompensating = false;
    }
}
