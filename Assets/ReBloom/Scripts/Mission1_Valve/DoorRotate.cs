using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class DoorRotate : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Transform interactor;

    private float previousZ;
    private float currentAngle;

    public float rotateDirection = 1f;
    public float rotateSpeed = 200f;

    public float minAngle = -90f;
    public float maxAngle = 90f;

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        currentAngle = transform.localEulerAngles.y;
        if (currentAngle > 180f) 
            currentAngle -= 360f;
    }

    void Update()
    {
        if (interactor == null)
            return;

        float currentZ = interactor.position.z;
        float delta = currentZ - previousZ;

        // 한 프레임에 너무 크게 변하는 값 제한
        delta = Mathf.Clamp(delta, -0.03f, 0.03f);

        currentAngle += -delta * rotateSpeed * rotateDirection;
        currentAngle = Mathf.Clamp(currentAngle, minAngle, maxAngle);

        transform.localRotation = Quaternion.Euler(0f, currentAngle, 0f);

        previousZ = currentZ;
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        Debug.Log("Grabbed Door");
        interactor = args.interactorObject.transform;
        previousZ = interactor.position.z;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }
}
