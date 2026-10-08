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
    private float previousHandAngle;
    private bool hasPreviousHandAngle;

    [Tooltip("클리어로 인정되는 회전 방향. 1 = +Z 회전, -1 = 반대 방향")]
    public float rotateDirection = 1f;

    [Tooltip("손이 밸브 중심에서 이 거리(m)보다 가까우면 각도를 읽지 않는다 (중심 근처에서 값이 튀는 것 방지)")]
    [SerializeField] private float centerDeadZone = 0.03f;

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

        // 밸브 중심을 기준으로 손이 몇 도 위치에 있는지 읽고, 그 변화량만큼 밸브를 돌린다.
        float handAngle;
        if (!TryGetHandAngle(out handAngle))
        {
            hasPreviousHandAngle = false;
            return;
        }

        if (!hasPreviousHandAngle)
        {
            previousHandAngle = handAngle;
            hasPreviousHandAngle = true;
            return;
        }

        float angleDelta = Mathf.DeltaAngle(previousHandAngle, handAngle);
        previousHandAngle = handAngle;

        if (Mathf.Approximately(angleDelta, 0f))
            return;

        // 한 프레임에 45도 이상은 튀는 값(텔레포트 등)으로 보고 무시
        if (Mathf.Abs(angleDelta) >= 45f)
            return;

        if (HasStateAuthority)
            ApplyAngle(angleDelta);
        else
            RPC_RequestRotate(angleDelta);
    }

    /// <summary>밸브 면(부모 기준 XY 평면) 위에서 손이 중심 기준 몇 도에 있는지.</summary>
    private bool TryGetHandAngle(out float angle)
    {
        Transform space = transform.parent;
        Vector3 local = space != null
            ? space.InverseTransformPoint(interactor.position) - transform.localPosition
            : interactor.position - transform.position;

        angle = 0f;
        if (new Vector2(local.x, local.y).sqrMagnitude < centerDeadZone * centerDeadZone)
            return false;

        angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        return true;
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

        float dir = rotateDirection < 0f ? -1f : 1f;
        float turned = (CurrentAngle - StartAngle) * dir; // 정해진 방향만 인정
        if (turned >= clearAngle)
        {
            CurrentAngle = StartAngle + clearAngle * dir;
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
        hasPreviousHandAngle = TryGetHandAngle(out previousHandAngle);
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
        hasPreviousHandAngle = false;

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
