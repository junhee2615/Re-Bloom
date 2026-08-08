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

    private float grabTime;

    [Header("Door Sound")]
    [SerializeField] private AudioSource doorMoveAudio;

    [SerializeField] private float soundAngleThreshold = 0.05f;
    [SerializeField] private float soundStartAngle = 10f;
    [SerializeField] private float soundStopDelay = 0.08f;

    private float previousSoundAngle;
    private float lastMovementTime;

    private float grabStartAngle;
    private bool soundStartedThisGrab;
    private bool isSoundPaused;


    public override void Spawned()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        Debug.Log($"[Door Spawned] " +
             $"Runner:{Runner.GameMode}, " +
             $"Local:{Runner.LocalPlayer}, " +
             $"StateAuthority:{Object.StateAuthority}, " +
             $"HasStateAuthority:{HasStateAuthority}, " +
             $"HasInputAuthority:{HasInputAuthority}");

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        if (HasStateAuthority)
        {
            float startAngle = transform.localEulerAngles.y;
            if (startAngle > 180f) startAngle -= 360f;
            CurrentAngle = startAngle;

            Debug.Log($"[Door Init] CurrentAngle:{CurrentAngle}");
        }

        if (doorMoveAudio == null)
            doorMoveAudio = GetComponent<AudioSource>();

        previousSoundAngle = CurrentAngle;
        lastMovementTime = Time.time;
    }

    private void Update()
    {
        if (interactor == null)
            return;

        float currentZ = interactor.position.z;
        float delta = currentZ - previousZ;

        Debug.Log($"[Update] Local:{Runner.LocalPlayer}, Delta:{delta}, HasStateAuthority:{HasStateAuthority}");

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
        previousZ = currentZ;
    }

    public override void Render()
    {
        transform.localRotation = Quaternion.Euler(0f, CurrentAngle, 0f);

        UpdateDoorSound();
    }

    private void ApplyAngle(float angleDelta)
    {
        float before = CurrentAngle;

        CurrentAngle += angleDelta;
        CurrentAngle = Mathf.Clamp(CurrentAngle, minAngle, maxAngle);

        Debug.Log($"[ApplyAngle] " +
              $"Local:{Runner.LocalPlayer}, " +
              $"Before:{before}, " +
              $"Delta:{angleDelta}, " +
              $"After:{CurrentAngle}");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRotate(float angleDelta, RpcInfo info = default)
    {
        Debug.Log($"[Host Received RPC] " +
              $"From:{info.Source}, " +
              $"Local:{Runner.LocalPlayer}, " +
              $"HasStateAuthority:{HasStateAuthority}, " +
              $"angleDelta:{angleDelta}");

        ApplyAngle(angleDelta);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        grabTime = Time.time;

        interactor = args.interactorObject.transform;
        previousZ = interactor.position.z;

        grabStartAngle = CurrentAngle;
        soundStartedThisGrab = false;

        Debug.Log($"[Grab] Time:{Time.time}, Local:{Runner.LocalPlayer}, " +
              $"Interactor:{interactor.name}, IsSelected:{grabInteractable.isSelected}");
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        Debug.Log($"[Release] Time:{Time.time}, HeldTime:{Time.time - grabTime}, " +
              $"Local:{Runner.LocalPlayer}, " +
              $"Interactor:{args.interactorObject.transform.name}, " +
              $"IsSelected:{grabInteractable.isSelected}");
        interactor = null;

        if (doorMoveAudio != null && doorMoveAudio.isPlaying)
        {
            doorMoveAudio.Pause();
            isSoundPaused = true;
        }

        soundStartedThisGrab = false;
    }

    private void UpdateDoorSound()
    {
        if (doorMoveAudio == null)
            return;

        float angleDifference = Mathf.Abs(
            Mathf.DeltaAngle(previousSoundAngle, CurrentAngle)
        );

        bool isDoorMoving =
            angleDifference > soundAngleThreshold;

        if (interactor != null && !soundStartedThisGrab)
        {
            float movedSinceGrab = Mathf.Abs(
                Mathf.DeltaAngle(grabStartAngle, CurrentAngle)
            );

            if (movedSinceGrab >= soundStartAngle)
            {
                soundStartedThisGrab = true;
            }
        }

        if (soundStartedThisGrab && isDoorMoving)
        {
            lastMovementTime = Time.time;

            if (isSoundPaused)
            {
                doorMoveAudio.UnPause();
                isSoundPaused = false;
            }
            else if (!doorMoveAudio.isPlaying)
            {
                doorMoveAudio.Play();
            }
        }
        else if (
            doorMoveAudio.isPlaying &&
            Time.time - lastMovementTime >= soundStopDelay
        )
        {
            doorMoveAudio.Pause();
            isSoundPaused = true;
        }

        previousSoundAngle = CurrentAngle;
    }

    private void OnDisable()
    {
        if (doorMoveAudio == null)
            return;

        doorMoveAudio.Stop();

        isSoundPaused = false;
        soundStartedThisGrab = false;
    }
}
