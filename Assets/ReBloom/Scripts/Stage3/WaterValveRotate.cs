using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.Events;

public class WaterValveRotate : NetworkBehaviour
{
    [Networked]
    public float CurrentAngle { get; set; }
    private XRGrabInteractable grabInteractable;
    private Transform interactor;
    private float previousZ; // Z로 해야 컨트롤러 위아래로 움직일때 밸브 움직임 
    public float rotateDirection = 1f;
    public float rotateSpeed = 200f;

    [Header("Valve Sound")]
    [SerializeField] private AudioSource valveMoveAudio;
    [SerializeField] private float soundAngleThreshold = 0.05f;
    [SerializeField] private float soundStopDelay = 0.08f;
    private float previousSoundAngle;
    private float lastMovementTime;
    private bool isSoundPaused;

    [Header("Clear")]
    [SerializeField] private float clearAngle = 360f;   // 시작 각도 기준으로 이만큼 돌리면 클리어
    public UnityEvent onValveCleared; // 인스펙터에서 이벤트 추가만 해주면 됨 
    [Networked] public NetworkBool IsCleared { get; set; }
    [Networked] private float StartAngle { get; set; }
    private ChangeDetector changes;


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
            StartAngle = startAngle;
        }
        changes = GetChangeDetector(ChangeDetector.Source.SimulationState);

        if (valveMoveAudio == null)
            valveMoveAudio = GetComponent<AudioSource>();

        previousSoundAngle = CurrentAngle;
        lastMovementTime = Time.time;
    }

    private void Update()
    {
        if (interactor == null)
            return;

        float currentZ = interactor.position.z;
        float delta = currentZ - previousZ;

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
        transform.localRotation = Quaternion.Euler(0f, 0f, CurrentAngle);
        UpdateValveSound();

        foreach (var change in changes.DetectChanges(this))
            if (change == nameof(IsCleared) && IsCleared)
            {
                grabInteractable.enabled = false;
                onValveCleared?.Invoke();
            }
    }

    private void ApplyAngle(float angleDelta)
    {
        if (IsCleared) return;                      // 클리어 후엔 더 안 돌아가게
        CurrentAngle += angleDelta;

        float turned = (CurrentAngle - StartAngle) * rotateDirection; // 정해진 방향만 인정
        if (turned >= clearAngle)
        {
            CurrentAngle = StartAngle + clearAngle * rotateDirection;
            IsCleared = true;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRotate(float angleDelta, RpcInfo info = default)
    {
        ApplyAngle(angleDelta);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        interactor = args.interactorObject.transform;
        previousZ = interactor.position.z;
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
