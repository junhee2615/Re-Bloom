using UnityEngine;
using Fusion;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class GeneratorDoorRotate : NetworkBehaviour
{
    [Networked]
    public float CurrentAngle { get; set; }

    private XRGrabInteractable grabInteractable;
    private Transform interactor;

    private float previousZ;

    public float rotateSpeed = 200f;

    public float minAngle = -90f;
    public float maxAngle = 90f;

    private float grabTime;

    private float startY;
    private float startZ;


    public override void Spawned()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        startY = transform.localEulerAngles.y;
        startZ = transform.localEulerAngles.z;

        if (HasStateAuthority)
        {
            float startAngle = transform.localEulerAngles.x;
            if (startAngle > 180f) startAngle -= 360f;
            CurrentAngle = startAngle;
        }
    }

    private void Update()
    {
        if (interactor == null)
            return;

        float currentZ = interactor.position.z;
        float delta = currentZ - previousZ;

        if (Mathf.Abs(delta) < 0.2f)
        {
            float angleDelta = -delta * rotateSpeed;

            if (HasStateAuthority)
                ApplyAngle(angleDelta);
            else
                RPC_RequestRotate(angleDelta);
        }
        else
        {
            Debug.Log($"[Delta Blocked] Delta:{delta}");
        }
        previousZ = currentZ;
    }

    public override void Render()
    {
        transform.localRotation = Quaternion.Euler(CurrentAngle, startY, startZ);
    }

    private void ApplyAngle(float angleDelta)
    {
        CurrentAngle += angleDelta;
        CurrentAngle = Mathf.Clamp(CurrentAngle, minAngle, maxAngle);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRotate(float angleDelta, RpcInfo info = default)
    {
        ApplyAngle(angleDelta);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        grabTime = Time.time;

        interactor = args.interactorObject.transform;
        previousZ = interactor.position.z;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }
}
