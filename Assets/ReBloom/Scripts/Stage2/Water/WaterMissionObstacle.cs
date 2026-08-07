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
///  - 호스트가 잡은 플레이어의 손 트랜스폼을 따라 오브젝트를 이동시키고,
///    최종 포즈를 [Networked] 로 모든 클라이언트에 전파한다.
///
/// 서브클래스가 이동 방식과 치워짐 조건만 구현하면 된다.
/// (흙=던지기 / 뿌리=당겨뽑기 / 돌=2인 협동)
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public abstract class WaterMissionObstacle : NetworkBehaviour
{
    [Header("공통 설정")]
    [Tooltip("움직이는 데 필요한 최소 손 개수.")]
    [SerializeField] protected int requiredGrabbers = 1;

    [Tooltip("치워지면 오브젝트를 완전히 제거한다. 끄면 콜라이더/렌더만 끈다.")]
    [SerializeField] protected bool despawnOnCleared = true;

    [Networked] public NetworkBool IsCleared { get; private set; }
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
    private Collider[] colliders;
    private Renderer[] renderers;

    private bool wasHeld;
    private Vector3 prevHandAPos;
    // 슬롯 A 손 속도
    protected float lastHandASpeed;

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

        colliders = GetComponentsInChildren<Collider>();
        renderers = GetComponentsInChildren<Renderer>();

        grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            // 감지 전용: 이동은 XRI 가 직접 움직이지 않게 한다.
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
        if (IsCleared) return;
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

    // ---------- 호스트 시뮬레이션 ----------

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || IsCleared) return;

        int count = GrabberCount;
        bool enough = count > 0 && HasEnoughGrabbers(count);

        if (enough)
        {
            Hands h = GatherHands();

            if (h.hasA)
            {
                if (wasHeld) lastHandASpeed = (h.posA - prevHandAPos).magnitude / Runner.DeltaTime;
                prevHandAPos = h.posA;
            }

            if (!wasHeld) { OnHeldBegin(h); wasHeld = true; }

            Vector3 p = NetPosition;
            Quaternion r = NetRotation;
            ComputeHeldPose(h, ref p, ref r);
            NetPosition = p;
            NetRotation = r;

            TryClearWhileHeld(p, r);
        }
        else if (wasHeld)
        {
            OnHeldEnd();
            wasHeld = false;
        }
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

        if (IsCleared)
        {
            ApplyCleared();
            return;
        }

        // 네트워크 포즈를 부드럽게 따라가기
        float k = 1f - Mathf.Exp(-18f * Time.deltaTime);
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, NetPosition, k),
            Quaternion.Slerp(transform.rotation, NetRotation, k));
    }

    /// <summary>호스트에서 호출: 이 장애물을 치워진 상태로 확정한다.</summary>
    protected void ClearObstacle()
    {
        if (IsCleared) return;
        IsCleared = true;

        if (despawnOnCleared && Object != null && Runner != null)
            Runner.Despawn(Object);
    }

    private bool clearedVisualApplied;
    protected virtual void ApplyCleared()
    {
        if (clearedVisualApplied) return;
        clearedVisualApplied = true;

        if (colliders != null)
            foreach (var c in colliders) if (c) c.enabled = false;
        if (renderers != null)
            foreach (var r in renderers) if (r) r.enabled = false;
    }

    // ---------- 서브클래스 훅 ----------

    /// <summary>충분한 인원이 잡았는지. 기본: requiredGrabbers 이상. 돌은 "서로 다른 2인"으로 재정의.</summary>
    protected virtual bool HasEnoughGrabbers(int count) => count >= requiredGrabbers;

    /// <summary>이번 프레임에 처음 들리기 시작했을 때.</summary>
    protected virtual void OnHeldBegin(Hands h) { }

    /// <summary>모든 손이 놓여 들림이 끝났을 때(흙 던지기 판정 등).</summary>
    protected virtual void OnHeldEnd() { }

    /// <summary>들린 동안 매 틱 목표 포즈를 계산한다.</summary>
    protected abstract void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot);

    /// <summary>들린 동안 매 틱 치워짐 조건을 검사한다.</summary>
    protected virtual void TryClearWhileHeld(Vector3 pos, Quaternion rot) { }
}
