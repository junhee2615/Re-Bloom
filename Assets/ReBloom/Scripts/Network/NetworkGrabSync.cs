using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 씬에 놓인 네트워크 잡기 물체(<see cref="XRGrabInteractable"/> + <see cref="NetworkObject"/> +
/// <see cref="NetworkTransform"/>)를 <b>클라이언트가 잡아도</b> 양쪽에 보이게 한다.
///
/// 왜 필요한가 — Host/Client 모드에서 씬 물체의 StateAuthority는 항상 호스트다.
/// <see cref="NetworkTransform"/>은 권한자(호스트)의 값만 복제하므로, 클라이언트가 XRI로 물체를 움직여도
///  ① 호스트에는 전달되지 않고(호스트에서 안 보임)
///  ② 클라이언트 자기 화면에서도 매 프레임 호스트 값으로 덮어써진다(소켓에 닿지 못해 노란 표시도 안 뜸).
///
/// 방식 — 수로 장애물(<c>WaterMissionObstacle</c>)과 같은 "호스트가 손을 따라간다" 구조다.
///  - 잡은 클라이언트: 자기 <see cref="NetworkTransform"/>을 끄고 XRI가 로컬로 움직이게 둔다(지연 없음).
///    그리고 "내 네트워크 손 기준 물체 자세(offset)"를 호스트에 보낸다.
///  - 호스트: 그 플레이어의 네트워크 손 × offset으로 물체를 옮긴다 → NetworkTransform이 그대로 복제.
///  - 놓을 때: 클라이언트가 최종 자세·속도를 보내고, 호스트가 그 자리에서 물리를 이어받는다.
///    호스트의 확인(잡은 사람 = 없음)이 돌아온 뒤에 NetworkTransform을 다시 켠다 — 먼저 켜면
///    아직 갱신 안 된 호스트 값으로 한 번 튄다.
///
/// 소켓 — 남이 잡고 있는 동안에는 내 기기의 <see cref="XRGrabInteractable"/>을 끈다.
/// 안 그러면 내 기기의 소켓이 남의 손에 들린 물체를 먼저 빼앗아 끼운다(호스트 쪽에서 실제로 그렇게 된다).
/// 놓은 뒤에는 양쪽 소켓이 각자 같은 자리에서 물체를 받아 끼우므로 결과가 같다.
///
/// 상태는 <see cref="NetworkPlayer"/>의 RPC로 오간다 — 이 컴포넌트는 NetworkBehaviour가 아니어서
/// 씬의 NetworkObject를 다시 베이크할 필요가 없다.
///
/// 배치: 잡기 물체 루트(XRGrabInteractable·NetworkObject와 같은 GameObject).
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[AddComponentMenu("ReBloom/Network/Network Grab Sync")]
public class NetworkGrabSync : MonoBehaviour
{
    [Header("잡는 동안 offset 전송 (클라이언트 → 호스트)")]
    [Tooltip("초당 최대 전송 횟수. 잡은 직후 XRI가 손에 붙이는 동안만 값이 바뀌므로 실제 전송은 드물다.")]
    [SerializeField, Min(1f)] private float offsetSendRate = 15f;

    [Tooltip("이보다 적게 바뀌면 보내지 않는다(m).")]
    [SerializeField, Min(0f)] private float offsetPositionEpsilon = 0.002f;

    [Tooltip("이보다 적게 바뀌면 보내지 않는다(도).")]
    [SerializeField, Min(0f)] private float offsetAngleEpsilon = 0.5f;

    [Header("놓을 때 넘기는 속도 상한 (프리팹의 Limit Linear/Angular Velocity와 같은 값)")]
    [SerializeField, Min(0f)] private float maxReleaseSpeed = 2.5f;
    [SerializeField, Min(0f)] private float maxReleaseAngularSpeed = 10f;

    [Header("디버그")]
    [SerializeField] private bool logToConsole = true;

    private static readonly List<NetworkGrabSync> Instances = new List<NetworkGrabSync>();

    private XRGrabInteractable grab;
    private NetworkObject netObject;
    private NetworkTransform netTransform;
    private Rigidbody body;

    // 호스트가 알려 준 "지금 잡고 있는 사람". 모든 피어가 같은 값을 갖는다.
    private PlayerRef holder = PlayerRef.None;
    private bool holderIsLeft;

    // (호스트) 원격 잡은 사람의 손 기준 물체 자세.
    private Vector3 remoteOffsetPos;
    private Quaternion remoteOffsetRot = Quaternion.identity;

    // 프리팹에 정해진 원래 키네마틱 여부. XRI·소켓이 잡는 동안 바꿔 놓으므로 시작 값을 기억한다.
    private bool naturalKinematic;

    // (로컬) 내 손에 들려 있는가.
    private bool localHeld;
    private bool localIsLeft;

    // (클라이언트) offset 전송 상태와 놓을 때 넘길 속도 추정.
    private float nextSendTime;
    private Vector3 lastSentPos;
    private Quaternion lastSentRot;
    private Vector3 prevPos;
    private Quaternion prevRot;
    private Vector3 velocity;
    private Vector3 angularVelocity;

    private bool clientSetupDone;

    private NetworkRunner Runner => netObject != null ? netObject.Runner : null;

    // 세션 안에서 스폰된 씬 물체인가. 아니면(세션 없음) XRI 기본 동작 그대로 둔다.
    private bool Online => Runner != null && Runner.IsRunning && netObject.Id.IsValid;

    private bool IsHost => Online && Runner.IsServer;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        netObject = GetComponent<NetworkObject>();
        netTransform = GetComponent<NetworkTransform>();
        body = GetComponent<Rigidbody>();
        naturalKinematic = body != null && body.isKinematic;

        if (netObject == null)
            Debug.LogWarning($"[NetGrab] {name}: NetworkObject가 없어 동기화하지 않습니다.", this);
    }

    private void OnEnable()
    {
        Instances.Add(this);
        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);
    }

    private void OnDisable()
    {
        Instances.Remove(this);
        grab.selectEntered.RemoveListener(OnSelectEntered);
        grab.selectExited.RemoveListener(OnSelectExited);
    }

    private static bool IsHand(IXRInteractor interactor) => !(interactor is XRSocketInteractor);

    // ── 로컬 잡기/놓기 ─────────────────────────────────────────────────────

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (!IsHand(args.interactorObject) || !Online) return;

        NetworkPlayer me = NetworkPlayer.LocalInstance;
        if (me == null) return;

        localHeld = true;
        localIsLeft = args.interactorObject.handedness == InteractorHandedness.Left;

        if (IsHost)
        {
            // 호스트가 잡으면 XRI가 움직이고 NetworkTransform이 그대로 복제한다. 알리기만 한다.
            me.Rpc_GrabHolder(netObject.Id, Runner.LocalPlayer, localIsLeft);
            return;
        }

        // 클라이언트: 내 화면은 XRI가 움직인다. 호스트 값으로 덮어쓰지 않게 끈다.
        if (netTransform != null) netTransform.enabled = false;

        prevPos = transform.position;
        prevRot = transform.rotation;
        velocity = Vector3.zero;
        angularVelocity = Vector3.zero;

        ComputeLocalOffset(out Vector3 pos, out Quaternion rot);
        me.Rpc_GrabReport(netObject.Id, localIsLeft, pos, rot);
        lastSentPos = pos;
        lastSentRot = rot;
        nextSendTime = Time.unscaledTime + 1f / offsetSendRate;

        Log($"잡음(클라이언트) — {(localIsLeft ? "왼손" : "오른손")}");
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (!IsHand(args.interactorObject) || !localHeld) return;

        localHeld = false;
        if (!Online) return;

        NetworkPlayer me = NetworkPlayer.LocalInstance;
        if (me == null) return;

        if (IsHost)
        {
            me.Rpc_GrabHolder(netObject.Id, PlayerRef.None, false);
            return;
        }

        // NetworkTransform은 여기서 켜지 않는다 — 호스트의 확인(OnHolderChanged)이 올 때 켠다.
        Vector3 v = Vector3.ClampMagnitude(velocity, maxReleaseSpeed);
        Vector3 w = Vector3.ClampMagnitude(angularVelocity, maxReleaseAngularSpeed);
        me.Rpc_GrabRelease(netObject.Id, transform.position, transform.rotation, v, w);

        Log($"놓음(클라이언트) — 속도 {v.magnitude:F2} m/s");
    }

    private void LateUpdate()
    {
        if (!Online) return;

        if (!IsHost)
        {
            SetupClientOnce();

            if (localHeld)
                UpdateLocalHold();

            return;
        }

        // 호스트: 원격 플레이어가 잡고 있으면 그 손을 따라간다.
        if (holder == PlayerRef.None || holder == Runner.LocalPlayer) return;
        if (!TryGetHand(holder, holderIsLeft, out Transform hand)) return;

        // 매번 확인한다 — 소켓에서 빼낸 경우 잡기를 끄는 순간 XRI가 원래(비키네마틱) 상태로 되돌려
        // 중력과 싸운다.
        if (body != null && !body.isKinematic) body.isKinematic = true;

        transform.SetPositionAndRotation(
            hand.position + hand.rotation * remoteOffsetPos,
            hand.rotation * remoteOffsetRot);
    }

    // 클라이언트에서는 물리를 돌리지 않는다 — 위치는 호스트가 정하고 NetworkTransform이 가져온다.
    // 로컬 중력이 남아 있으면 복제값과 싸운다. XRI 던지기도 끈다(키네마틱에 속도를 줄 수 없고,
    // 던지기는 호스트가 놓을 때 받은 속도로 한다).
    private void SetupClientOnce()
    {
        if (clientSetupDone) return;
        clientSetupDone = true;

        if (body != null) body.isKinematic = true;
        grab.throwOnDetach = false;
    }

    private void UpdateLocalHold()
    {
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            velocity = (transform.position - prevPos) / dt;

            Quaternion delta = transform.rotation * Quaternion.Inverse(prevRot);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            angularVelocity = float.IsFinite(axis.x) ? axis * (angle * Mathf.Deg2Rad / dt) : Vector3.zero;
        }
        prevPos = transform.position;
        prevRot = transform.rotation;

        if (Time.unscaledTime < nextSendTime) return;
        nextSendTime = Time.unscaledTime + 1f / offsetSendRate;

        ComputeLocalOffset(out Vector3 pos, out Quaternion rot);
        if ((pos - lastSentPos).magnitude < offsetPositionEpsilon &&
            Quaternion.Angle(rot, lastSentRot) < offsetAngleEpsilon)
            return;

        NetworkPlayer me = NetworkPlayer.LocalInstance;
        if (me == null) return;

        me.Rpc_GrabOffset(netObject.Id, pos, rot);
        lastSentPos = pos;
        lastSentRot = rot;
    }

    // 내 "네트워크" 손 기준 물체 자세. 호스트도 같은 네트워크 손을 보므로 기준이 일치한다.
    private void ComputeLocalOffset(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;

        NetworkPlayer me = NetworkPlayer.LocalInstance;
        Transform hand = me == null ? null : (localIsLeft ? me.LeftHand : me.RightHand);
        if (hand == null) return;

        Quaternion inv = Quaternion.Inverse(hand.rotation);
        pos = inv * (transform.position - hand.position);
        rot = inv * transform.rotation;
    }

    private static bool TryGetHand(PlayerRef player, bool isLeft, out Transform hand)
    {
        hand = null;
        if (!NetworkPlayer.TryGet(player, out NetworkPlayer np) || np == null) return false;

        hand = isLeft ? np.LeftHand : np.RightHand;
        return hand != null;
    }

    // ── RPC 수신 (NetworkPlayer가 부른다) ─────────────────────────────────

    private static bool TryFind(NetworkId id, out NetworkGrabSync sync)
    {
        foreach (NetworkGrabSync s in Instances)
        {
            if (s != null && s.netObject != null && s.netObject.Id == id)
            {
                sync = s;
                return true;
            }
        }

        sync = null;
        return false;
    }

    /// <summary>(호스트) 클라이언트가 잡았다.</summary>
    public static void HostReceiveGrab(NetworkId id, PlayerRef player, bool isLeft, Vector3 offsetPos, Quaternion offsetRot)
    {
        if (!TryFind(id, out NetworkGrabSync s) || !s.IsHost) return;

        NetworkPlayer host = NetworkPlayer.LocalInstance;
        if (host == null) return;

        // 이미 다른 사람이 잡고 있으면 거절하고 현재 주인을 다시 알린다(그쪽은 손에서 떨어진다).
        if (s.holder != PlayerRef.None && s.holder != player)
        {
            host.Rpc_GrabHolder(id, s.holder, s.holderIsLeft);
            return;
        }

        s.remoteOffsetPos = offsetPos;
        s.remoteOffsetRot = offsetRot;

        if (s.body != null) s.body.isKinematic = true;

        host.Rpc_GrabHolder(id, player, isLeft);
        s.Log($"원격 잡음 — {player}");
    }

    /// <summary>(호스트) 잡은 클라이언트의 손 기준 자세가 바뀌었다.</summary>
    public static void HostReceiveOffset(NetworkId id, PlayerRef player, Vector3 offsetPos, Quaternion offsetRot)
    {
        if (!TryFind(id, out NetworkGrabSync s) || !s.IsHost || s.holder != player) return;

        s.remoteOffsetPos = offsetPos;
        s.remoteOffsetRot = offsetRot;
    }

    /// <summary>(호스트) 잡은 클라이언트가 놓았다. 그 자리·속도에서 물리를 이어받는다.</summary>
    public static void HostReceiveRelease(NetworkId id, PlayerRef player, Vector3 pos, Quaternion rot, Vector3 vel, Vector3 angVel)
    {
        if (!TryFind(id, out NetworkGrabSync s) || !s.IsHost || s.holder != player) return;

        NetworkPlayer host = NetworkPlayer.LocalInstance;
        if (host == null) return;

        s.transform.SetPositionAndRotation(pos, rot);

        if (s.body != null)
        {
            s.body.position = pos;
            s.body.rotation = rot;

            s.body.isKinematic = s.naturalKinematic;

            if (!s.body.isKinematic)
            {
                s.body.linearVelocity = vel;
                s.body.angularVelocity = angVel;
            }
        }

        // 잡기를 다시 켜는 것은 OnHolderChanged가 한다. 켜지는 순간 소켓 범위 안이면 호스트 소켓이 받는다.
        host.Rpc_GrabHolder(id, PlayerRef.None, false);
        s.Log($"원격 놓음 — {player}");
    }

    /// <summary>(모든 피어) 잡은 사람이 바뀌었다. 호스트가 알린다.</summary>
    public static void OnHolderChanged(NetworkId id, PlayerRef newHolder, bool isLeft)
    {
        if (!TryFind(id, out NetworkGrabSync s) || !s.Online) return;

        s.holder = newHolder;
        s.holderIsLeft = isLeft;

        PlayerRef me = s.Runner.LocalPlayer;
        bool heldByOther = newHolder != PlayerRef.None && newHolder != me;

        // 남이 잡고 있는 동안에는 내 손도, 내 소켓도 이 물체를 건드리지 못하게 한다.
        // (잡고 있던 중이면 XRI가 놓기 처리를 한다 — 동시에 잡은 경우 호스트가 정한 쪽만 남는다)
        s.grab.enabled = !heldByOther;

        // 클라이언트: 내가 주인이 아니게 됐으면(놓음 확인, 또는 거절) 다시 복제값을 따른다.
        if (!s.IsHost && newHolder != me && s.netTransform != null)
            s.netTransform.enabled = true;
    }

    private void Log(string message)
    {
        if (logToConsole) Debug.Log($"[NetGrab] {name}: {message}", this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instances.Clear();
}
