using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 수로를 막는 장애물 공통 베이스.
///
/// "호스트 권위" 방식:
///  - 로컬에서는 <see cref="XRGrabInteractable"/> 을 "잡음 감지 전용"으로만 사용한다
///    (trackPosition/trackRotation 을 꺼서 XRI 가 트랜스폼을 직접 움직이지 못하게 한다).
///  - 잡음/놓음을 RPC 로 StateAuthority(호스트)에 알린다.
///  - 잡은 동안에는 호스트에서 손 쪽으로 "속도"를 줘서 따라오게 한다(velocity tracking).
///  - 놓으면 그 순간의 속도로 자연스럽게 날아가/떨어진다.
///  - 트랜스폼은 항상 호스트가 소유하고, 최종 포즈를 [Networked] 로 전파한다.
///    클라이언트는 그 포즈를 Render() 에서 부드럽게 따라 그리기만 한다.
///
/// 서브클래스는 "어떻게 들리는지(ComputeHeldPose)"와 필요 시 "놓았을 때 동작(OnReleased)"만 구현한다.
/// (흙=던지기 / 돌=2인 협동 / 뿌리=들어 옮기기)
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public abstract class WaterMissionObstacle : NetworkBehaviour
{
    [Header("공통 설정")]
    [Tooltip("움직이는 데 필요한 최소 손 개수.")]
    [SerializeField] protected int requiredGrabbers = 1;

    [Tooltip("원위치보다 이만큼(m) 더 아래로 떨어지면 낙하를 멈춘다(무한 낙하 방지).")]
    [SerializeField] private float fallSafetyDepth = 15f;

    [Tooltip("잡은 손이 물체 표면에서 이 거리(m)보다 멀어지면 그 손을 놓는다.")]
    [SerializeField] private float releaseDistance = 0.5f;

    [Header("낙하 물리")]
    [Tooltip("낙하 중력 배수. 1 = 기본, 작을수록 가볍고 둥실 떠서 멀리 간다.")]
    [SerializeField] private float gravityScale = 1f;

    // 잡고 따라올 때 속도 상한(m/s). 손을 아무리 빨리 움직여도 물리가 폭주하지 않게 제한.
    private const float TrackMaxSpeed = 30f;

    [Networked] private NetworkBool Initialized { get; set; }
    [Networked] private Vector3 NetPosition { get; set; }
    [Networked] private Quaternion NetRotation { get; set; }

    // 잡은 손 슬롯(최대 2). PlayerRef.None = 빈 슬롯.
    [Networked] private PlayerRef GrabberA { get; set; }
    [Networked] private NetworkBool GrabberAIsLeft { get; set; }
    [Networked] private PlayerRef GrabberB { get; set; }
    [Networked] private NetworkBool GrabberBIsLeft { get; set; }

    protected Vector3 originPosition;
    protected Quaternion originRotation;

    private XRGrabInteractable grab;
    private Rigidbody body;
    private Collider[] bodyColliders;   // 손↔물체 표면 거리 측정용

    private enum State { Idle, Tracking, Falling } // 물체 상태
    private State state = State.Idle;
    private Vector3 trackTargetPos;       // 이번 틱의 손 추종 목표 위치
    private Quaternion trackTargetRot;    // 이번 틱의 손 추종 목표 회전

    /// <summary>호스트가 수집한, 현재 잡고 있는 손들의 월드 포즈.</summary>
    protected struct Hands
    {
        public bool hasA; public Vector3 posA; public Quaternion rotA;
        public bool hasB; public Vector3 posB; public Quaternion rotB;

        public Vector3 Midpoint =>
            (hasA && hasB) ? (posA + posB) * 0.5f : (hasA ? posA : posB);
    }

    protected int GrabberCount
    {
        get
        {
            int c = 0;
            if (GrabberA != PlayerRef.None) c++;
            if (GrabberB != PlayerRef.None) c++;
            return c;
        }
    }

    protected PlayerRef GrabberPlayerA => GrabberA;
    protected PlayerRef GrabberPlayerB => GrabberB;

    public override void Spawned()
    {
        originPosition = transform.position;
        originRotation = transform.rotation;

        body = GetComponent<Rigidbody>();
        bodyColliders = GetComponentsInChildren<Collider>();

        grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            // 감지 전용: 이동은 XRI 가 직접 하지 않게 한다.
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.throwOnDetach = false;
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }

        if (HasStateAuthority)
        {
            NetPosition = originPosition;
            NetRotation = originRotation;
            Initialized = true;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
        }
    }

    // ---------- 로컬 잡기 감지 → 호스트에 요청 ----------

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        bool isLeft = args.interactorObject.handedness == InteractorHandedness.Left;
        RPC_RequestGrab(Runner.LocalPlayer, isLeft);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        bool isLeft = args.interactorObject.handedness == InteractorHandedness.Left;
        RPC_RequestRelease(Runner.LocalPlayer, isLeft);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestGrab(PlayerRef player, NetworkBool isLeft)
    {
        if (Same(GrabberA, GrabberAIsLeft, player, isLeft)) return;
        if (Same(GrabberB, GrabberBIsLeft, player, isLeft)) return;

        if (GrabberA == PlayerRef.None) { GrabberA = player; GrabberAIsLeft = isLeft; }
        else if (GrabberB == PlayerRef.None) { GrabberB = player; GrabberBIsLeft = isLeft; }
        // 두 슬롯이 모두 차 있으면 무시
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestRelease(PlayerRef player, NetworkBool isLeft)
    {
        if (Same(GrabberA, GrabberAIsLeft, player, isLeft)) { GrabberA = PlayerRef.None; GrabberAIsLeft = false; }
        else if (Same(GrabberB, GrabberBIsLeft, player, isLeft)) { GrabberB = PlayerRef.None; GrabberBIsLeft = false; }
    }

    private static bool Same(PlayerRef a, NetworkBool aLeft, PlayerRef b, NetworkBool bLeft)
        => a != PlayerRef.None && a == b && aLeft == bLeft;

    // 잡은 손이 물체 표면에서 releaseDistance 를 넘어 멀어지면 그 손 슬롯을 비운다.
    private void PruneDistantGrabbers(in Hands h)
    {
        if (releaseDistance <= 0f || GrabberCount < 2) return;

        if (GrabberA != PlayerRef.None && h.hasA && HandTooFar(h.posA))
        {
            GrabberA = PlayerRef.None; GrabberAIsLeft = false;
        }
        if (GrabberB != PlayerRef.None && h.hasB && HandTooFar(h.posB))
        {
            GrabberB = PlayerRef.None; GrabberBIsLeft = false;
        }
    }

    // 손이 물체 표면에서 releaseDistance 보다 멀리 있으면 true.
    private bool HandTooFar(Vector3 handPos)
    {
        float best = float.PositiveInfinity;
        if (bodyColliders != null)
        {
            foreach (Collider c in bodyColliders)
            {
                if (c == null || c.isTrigger) continue;
                // ClosestPoint: 콜라이더가 프리미티브/컨벡스면 표면점,
                // 논컨벡스 MeshCollider면 입력점을 그대로 반환(거리 0)
                float d = Vector3.Distance(handPos, c.ClosestPoint(handPos));
                if (d < best) best = d;
            }
        }
        if (float.IsPositiveInfinity(best))
            best = Vector3.Distance(handPos, transform.position); // 콜라이더가 없으면 중심 거리로 대체

        return best > releaseDistance;
    }

    // ---------- 호스트 시뮬레이션 ----------

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority) return;

        Hands h = GatherHands();
        PruneDistantGrabbers(in h);   // 너무 멀어진(닿지 않은) 손은 놓는다

        int count = GrabberCount;
        bool held = HasEnoughGrabbers(count);

        if (held)
        {
            if (state != State.Tracking)
            {
                StartTracking();      // 속도 추종 시작
                OnHeldBegin(h);
            }

            // 이번 틱 목표 포즈 계산 → FixedUpdate 에서 이 목표로 속도를 준다.
            Vector3 p = transform.position;
            Quaternion r = transform.rotation;
            ComputeHeldPose(h, ref p, ref r);
            trackTargetPos = p;
            trackTargetRot = r;
        }
        else
        {
            if (state == State.Tracking)
            {
                state = State.Idle;   // 추종 종료.
                // 놓는 순간의 속도를 그대로 넘긴다 → 자연 낙하/던지기.
                OnReleased(body != null ? body.linearVelocity : Vector3.zero);
            }

            if (state == State.Falling)
            {
                EnsureDynamic();   // XRI 가 kinematic 을 되돌리는 경우 대비

                // 물리 종료.
                if (transform.position.y < originPosition.y - fallSafetyDepth)
                    EndPhysics();
                else if (body != null && body.IsSleeping())
                    EndPhysics();
            }
        }

        // 호스트의 최종 포즈를 클라이언트에 전파
        NetPosition = transform.position;
        NetRotation = transform.rotation;
    }

    // 잡고 따라오는 동안 물리로 손을 추종한다(호스트).
    // 커스텀 중력(gravityScale)도 낙하 중에 여기서 적용한다.
    private void FixedUpdate()
    {
        if (body == null || !HasStateAuthority) return;

        if (state == State.Tracking)
        {
            EnsureDynamic();

            float dt = Time.fixedDeltaTime;

            // 선형: 목표까지의 오차를 속도로.
            Vector3 v = (trackTargetPos - body.position) / dt;
            body.linearVelocity = Vector3.ClampMagnitude(v, TrackMaxSpeed);

            // 각속도: 목표 회전까지 남은 회전을 각속도로.
            Quaternion delta = trackTargetRot * Quaternion.Inverse(body.rotation); // 남은 회전 구하기
            delta.ToAngleAxis(out float angleDeg, out Vector3 axis); // 회전을 축, 각도로 분해
            if (angleDeg > 180f) angleDeg -= 360f; // 짧은 쪽으로 돌게 보정
            if (Mathf.Abs(angleDeg) > 0.05f && !float.IsNaN(axis.x) && !float.IsInfinity(axis.x))
                // 각속도로 변환해서 대입 : 축 방향 벡터 × 초당 라디안(회전 속력)
                body.angularVelocity = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);
            else
                body.angularVelocity = Vector3.zero;

            return;
        }

        // 낙하 중 커스텀 중력. gravityScale 이 1이 아닐 때만.
        // 이것도 클라에서 필요없나?
        if (state == State.Falling && !body.isKinematic && !Mathf.Approximately(gravityScale, 1f))
            body.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
    }

    private Hands GatherHands()
    {
        Hands h = new Hands();
        h.hasA = TryHand(GrabberA, GrabberAIsLeft, out h.posA, out h.rotA);
        h.hasB = TryHand(GrabberB, GrabberBIsLeft, out h.posB, out h.rotB);
        return h;
    }

    private bool TryHand(PlayerRef p, NetworkBool isLeft, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = Quaternion.identity;
        if (p == PlayerRef.None) return false;
        if (!NetworkPlayer.TryGet(p, out var np)) return false;

        Transform hand = isLeft ? np.LeftHand : np.RightHand;
        if (hand == null) return false;

        pos = hand.position;
        rot = hand.rotation;
        return true;
    }

    public override void Render()
    {
        if (!Initialized) return;

        if (HasStateAuthority) return;

        // 클라이언트: 네트워크 포즈를 부드럽게 따라간다.
        float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, NetPosition, k),
            Quaternion.Slerp(transform.rotation, NetRotation, k));
    }

    // ---------- 물리 상태 전환 : 호스트에서만 ----------

    // 물리 시뮬레이션용 non-kinematic 을 보장한다. XRI 등이 kinematic 으로 되돌릴 수 있어
    // 진입 시점과 추종/낙하 중 매 틱 이걸로 확인한다.
    private void EnsureDynamic()
    {
        if (body != null && body.isKinematic) body.isKinematic = false;
    }

    // 잡기 시작: non-kinematic + 중력 off 로 두고 속도 추종을 시작한다.
    private void StartTracking()
    {
        state = State.Tracking;
        EnsureDynamic();
        if (body != null)
        {
            body.useGravity = false;
            body.WakeUp();
        }
    }

    /// <summary>놓았을 때 실제 물리로 떨어지게 한다.</summary>
    protected void BeginPhysics(Vector3 velocity)
    {
        if (body == null) return;
        EnsureDynamic();
        // gravityScale 이 1이면 기본 중력, 아니면 위 FixedUpdate 에서 커스텀 중력을 적용한다.
        body.useGravity = Mathf.Approximately(gravityScale, 1f);
        body.linearVelocity = velocity;
        body.WakeUp();
        state = State.Falling;
    }

    private void EndPhysics()
    {
        state = State.Idle;
        SetKinematic(true);
    }

    private void SetKinematic(bool kinematic)
    {
        if (body == null) return;
        if (body.isKinematic != kinematic) body.isKinematic = kinematic;
        if (kinematic)
        {
            body.useGravity = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // ---------- 서브클래스 훅 ----------

    /// <summary>충분한 인원이 잡았는지. 기본: requiredGrabbers 이상. 돌은 "서로 다른 2인"으로 재정의.</summary>
    protected virtual bool HasEnoughGrabbers(int count) => count >= requiredGrabbers;

    /// <summary>이번 프레임에 처음 들리기 시작했을 때.</summary>
    protected virtual void OnHeldBegin(Hands h) { }

    /// <summary>모든 손이 놓였을 때.</summary>
    protected virtual void OnReleased(Vector3 velocity) => BeginPhysics(velocity);

    /// <summary>들린 동안 매 틱 목표 포즈를 계산한다.</summary>
    protected abstract void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot);
}
