using Fusion;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class ValveRotate : NetworkBehaviour
{
    [Networked]
    public float CurrentAngle { get; set; }

    private XRGrabInteractable grabInteractable;
    private Transform interactor;

    private float previousX;

    public float rotateDirection = 1f;
    public float rotateSpeed = 200f;

    private float grabTime;

    [Header("Valve Sound")]
    [SerializeField] private AudioSource valveMoveAudio;

    [SerializeField] private float soundAngleThreshold = 0.05f;
    [SerializeField] private float soundStopDelay = 0.08f;

    private float previousSoundAngle;
    private float lastMovementTime;
    private bool isSoundPaused;

    public override void Spawned()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        if (HasStateAuthority)
        {
            float startAngle = transform.localEulerAngles.z;
            if (startAngle > 180f) startAngle -= 360f;
            CurrentAngle = startAngle;
        }

        if (valveMoveAudio == null)
            valveMoveAudio = GetComponent<AudioSource>();

        previousSoundAngle = CurrentAngle;
        lastMovementTime = Time.time;
    }

    private void Update()
    {
        if (interactor == null)
            return;

        float currentX = interactor.position.x;
        float delta = currentX - previousX;

        if (Mathf.Abs(delta) < 0.2f)
        {
            float angleDelta = -delta * rotateSpeed * rotateDirection;

            if (HasStateAuthority)
                ApplyAngle(angleDelta);
            else
                RPC_RequestRotate(angleDelta);
        }
        else
        {
            Debug.Log($"[Delta Blocked] Delta:{delta}");
        }

        previousX = currentX;
    }

    public override void Render()
    {
        transform.localRotation = Quaternion.Euler(0f, 0f, CurrentAngle);

        UpdateValveSound();
    }

    private void ApplyAngle(float angleDelta)
    {
        CurrentAngle += angleDelta;
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
        previousX = interactor.position.x;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;

        if (valveMoveAudio != null && valveMoveAudio.isPlaying)
        {
            valveMoveAudio.Pause();
            isSoundPaused = true;
        }
    }

    private void UpdateValveSound()
    {
        if (valveMoveAudio == null)
            return;

        float angleDifference = Mathf.Abs(
            Mathf.DeltaAngle(previousSoundAngle, CurrentAngle)
        );

        bool isValveMoving =
            angleDifference > soundAngleThreshold;

        if (isValveMoving)
        {
            lastMovementTime = Time.time;

            if (isSoundPaused)
            {
                valveMoveAudio.UnPause();
                isSoundPaused = false;
            }
            else if (!valveMoveAudio.isPlaying)
            {
                valveMoveAudio.Play();
            }
        }
        else if (
            valveMoveAudio.isPlaying &&
            Time.time - lastMovementTime >= soundStopDelay
        )
        {
            valveMoveAudio.Pause();
            isSoundPaused = true;
        }

        previousSoundAngle = CurrentAngle;
    }
}
