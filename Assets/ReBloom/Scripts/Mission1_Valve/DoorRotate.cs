using Unity.VisualScripting;
using UnityEngine;
using Fusion;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class DoorRotate : NetworkBehaviour
{
    [Networked]
    public float CurrentAngle { get; set; }

    private XRGrabInteractable grabInteractable;
    private Transform interactor;

    private float previousZ;

    private bool wasStateAuthority = false; // 이전 Authority 상태 추적

    public float rotateDirection = 1f;
    public float rotateSpeed = 200f;

    public float minAngle = -90f;
    public float maxAngle = 90f;


    public override void Spawned()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        float startAngle = transform.localEulerAngles.y;
        if (startAngle > 180f)
            startAngle -= 360f;

        if (HasStateAuthority)
            CurrentAngle = startAngle;

        wasStateAuthority = HasStateAuthority;
    }

    public override void FixedUpdateNetwork()
    {
        // Authority 획득한 순간 감지
        if (HasStateAuthority && !wasStateAuthority)
        {
            if (interactor != null)
                previousZ = interactor.position.z; // 튀는 값 방지용 초기화
        }
        wasStateAuthority = HasStateAuthority;

        if (HasStateAuthority && interactor != null)
        {
            float currentZ = interactor.position.z;
            float delta = currentZ - previousZ;

            // Authority 막 받은 직후 튀는 값 방어
            if (Mathf.Abs(delta) < 0.03f)
            {
                CurrentAngle += -delta * rotateSpeed * rotateDirection;
                CurrentAngle = Mathf.Clamp(CurrentAngle, minAngle, maxAngle);
            }
            previousZ = currentZ; // 항상 갱신해서 튀는 값 방지
        }
        transform.localRotation = Quaternion.Euler(0f, CurrentAngle, 0f);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {   
        interactor = args.interactorObject.transform;
        previousZ = interactor.position.z;

        if (Object != null && !HasStateAuthority)
            Object.RequestStateAuthority();
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }
}
