using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class LeverSwitch : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Transform interactor;
    private float previousY;
    public bool isActivated = false;

    [Header("Lever Settings")]
    public float rotateSpeed = 200f;

    public float activationAngle = -170;

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);
    }

    void Update()
    {
        if (interactor != null)
        {
            // 컨트롤러 현재 위치
            float currentY = interactor.position.y;

            // 이동량 계산
            float delta = currentY - previousY;

            // 레버 회전
            transform.Rotate(Vector3.forward,
                             -delta * rotateSpeed,
                             Space.Self);

            previousY = currentY;

            // 현재 회전값 확인
            float angle = transform.localEulerAngles.z;

            if (angle > 180)
                angle -= 360;

            // 레버 활성화 판정
            if (angle <= activationAngle && !isActivated)
            {
                isActivated = true;

                Debug.Log(gameObject.name + " Activated");
            }
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        interactor = args.interactorObject.transform;
        previousY = interactor.position.y;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }
}