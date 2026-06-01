using Fusion;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ValveRotate : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;

    private Transform interactor;
    private bool isHolding = false;

    private float previousX;

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);
    }

    void Update()
    {
        // Client면 밸브 조작 불가
        // if (NetworkRunner.Instances[0].IsSharedModeMasterClient)
        //  return;

        if (isHolding && interactor != null)
        {
            float currentX = interactor.position.x;

            float delta = currentX - previousX;

            // 밸브 회전
            transform.Rotate(Vector3.forward, -delta * 200f, Space.Self);

            previousX = currentX;
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        isHolding = true;

        interactor = args.interactorObject.transform;

        previousX = interactor.position.x;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        isHolding = false;

        interactor = null;
    }
}
