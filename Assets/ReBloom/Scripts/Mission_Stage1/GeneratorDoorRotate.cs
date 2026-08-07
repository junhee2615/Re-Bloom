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

    private float previousY;

    [Header("Door Rotation")]
    public float rotateSpeed = 200f;
    public float minAngle = -90f;
    public float maxAngle = 90f;

    private float startY;
    private float startZ;

    [Header("Door Sound")]
    [SerializeField] private AudioSource doorMoveAudio;

    [Tooltip("한 프레임 동안 이 각도보다 많이 움직였을 때 문이 움직이는 것으로 판단합니다.")]
    [SerializeField] private float soundAngleThreshold = 0.05f;

    [Tooltip("문을 잡은 뒤 이 각도 이상 움직여야 소리가 처음 시작됩니다.")]
    [SerializeField] private float soundStartAngle = 3f;

    [Tooltip("문이 멈춘 후 소리를 일시정지하기까지의 시간입니다.")]
    [SerializeField] private float soundStopDelay = 0.08f;

    private float previousSoundAngle;
    private float lastMovementTime;

    private float grabStartAngle;
    private bool soundStartedThisGrab;
    private bool isSoundPaused;

    public override void Spawned()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        if (doorMoveAudio == null)
            doorMoveAudio = GetComponent<AudioSource>();

        startY = transform.localEulerAngles.y;
        startZ = transform.localEulerAngles.z;

        if (HasStateAuthority)
        {
            float startAngle = transform.localEulerAngles.x;

            if (startAngle > 180f)
                startAngle -= 360f;

            CurrentAngle = startAngle;
        }

        previousSoundAngle = CurrentAngle;
        lastMovementTime = Time.time;
    }

    private void Update()
    {
        if (interactor == null)
            return;

        // 손의 위/아래 움직임 사용
        float currentY = interactor.position.y;
        float delta = currentY - previousY;

        if (Mathf.Abs(delta) < 0.2f)
        {
            // 손을 위로 올리면 문이 위로 열리도록 방향 반전
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

        previousY = currentY;
    }

    public override void Render()
    {
        transform.localRotation =
            Quaternion.Euler(CurrentAngle, startY, startZ);

        UpdateDoorSound();
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

        // Grab한 위치에서 일정 각도 이상 움직였는지 확인
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

        // 실제로 움직이고 있을 때만 사운드 재생
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

    private void ApplyAngle(float angleDelta)
    {
        CurrentAngle += angleDelta;
        CurrentAngle = Mathf.Clamp(
            CurrentAngle,
            minAngle,
            maxAngle
        );
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRotate(
        float angleDelta,
        RpcInfo info = default
    )
    {
        ApplyAngle(angleDelta);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        interactor = args.interactorObject.transform;

        previousY = interactor.position.y;

        // 사운드 시작 판단용
        grabStartAngle = CurrentAngle;
        soundStartedThisGrab = false;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;

        if (doorMoveAudio != null &&
            doorMoveAudio.isPlaying)
        {
            doorMoveAudio.Pause();
            isSoundPaused = true;
        }

        soundStartedThisGrab = false;
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