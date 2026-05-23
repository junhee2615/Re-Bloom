using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class DoorRotate : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Transform interactor;
    private float previousZ;

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
            float currentZ = interactor.position.z;

            float delta = currentZ - previousZ;

            // 문 회전
            transform.Rotate(Vector3.up, delta * 200f, Space.Self);
            previousZ = currentZ;
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        interactor = args.interactorObject.transform;
        previousZ = interactor.position.z;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }
}
