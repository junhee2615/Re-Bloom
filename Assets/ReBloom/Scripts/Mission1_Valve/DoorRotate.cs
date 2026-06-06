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
    }

    public override void FixedUpdateNetwork()
    {
        Debug.Log($"Fixed / State:{HasStateAuthority}, Interactor:{interactor != null}, Angle:{CurrentAngle}");
        if (HasStateAuthority && interactor != null)
        {
            float currentZ = interactor.position.z;
            float delta = currentZ - previousZ;

            // 한 프레임에 너무 크게 변하는 값 제한
            delta = Mathf.Clamp(delta, -0.03f, 0.03f);

            CurrentAngle += -delta * rotateSpeed * rotateDirection;
            CurrentAngle = Mathf.Clamp(CurrentAngle, minAngle, maxAngle);

            previousZ = currentZ;
        }
        transform.localRotation = Quaternion.Euler(0f, CurrentAngle, 0f);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        Debug.Log("Grabbed Door");
        
        interactor = args.interactorObject.transform;
        previousZ = interactor.position.z;

        if (Object != null && !HasStateAuthority)
        {
            Object.RequestStateAuthority();
            Debug.Log("StateAuthority 요청함");
        }
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }
}
