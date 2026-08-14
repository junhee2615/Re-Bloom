using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>공명 접촉에 쓰는 손 구분(닿은 손 기준).</summary>
public enum Hand : byte { None, Left, Right }

public class NetworkPlayer : NetworkBehaviour
{
    // 내 플레이어인지 확인
    public bool IsLocalNetworkRig => Object != null && Object.HasInputAuthority;
    public HardwareRig HardwareRig => hardwareRig;
    public Transform PlayerTransform => playerTransform != null ? playerTransform.transform : null;

    // PlayerRef → NetworkPlayer 레지스트리.
    // 호스트가 "어떤 플레이어가 잡았는지"만 알아도 그 플레이어의 손 트랜스폼을
    // 직접 참조할 수 있도록 모든 인스턴스를 등록해 둔다. (WaterMissionObstacle 등에서 사용)
    private static readonly Dictionary<PlayerRef, NetworkPlayer> Players = new Dictionary<PlayerRef, NetworkPlayer>();

    public static bool TryGet(PlayerRef player, out NetworkPlayer networkPlayer)
        => Players.TryGetValue(player, out networkPlayer);

    // 등록된 인스턴스만 순회하기 위한 접근자
    public static Dictionary<PlayerRef, NetworkPlayer>.ValueCollection All => Players.Values;

    public Transform LeftHand => leftHandTransform != null ? leftHandTransform.transform : null;
    public Transform RightHand => rightHandTransform != null ? rightHandTransform.transform : null;
    public bool HasNetworkStateAuthority => Object != null && Object.IsValid && Object.HasStateAuthority;

    /// <summary>이 기기의 로컬(입력 권한 보유) 플레이어 인스턴스. 없으면 null.</summary>
    public static NetworkPlayer LocalInstance { get; private set; }


        /// <summary>
    /// 이 플레이어의 Role(mental / ear).
    /// 서버가 스폰 직전에 확정하며 모든 클라이언트에 동기화된다.
    /// (PlayerSpawner.SpawnPlayer -> AssignRole)
    /// </summary>
    [Networked] public Role AssignedRole { get; private set; }

    /// <summary>이 기기 로컬 플레이어의 Role. 아직 스폰 전이면 null.</summary>
    public static Role? LocalRole =>
        LocalInstance != null ? LocalInstance.AssignedRole : (Role?)null;

    /// <summary>(서버 전용) 스폰 시점에 Role을 확정한다.</summary>
    public void AssignRole(Role role)
    {
        AssignedRole = role;
    }

    /// <summary>해당 Role을 가진 플레이어를 찾는다. 없으면 false.</summary>
    public static bool TryGetByRole(Role role, out NetworkPlayer networkPlayer)
    {
        foreach (var candidate in Players.Values)
        {
            if (candidate != null && candidate.AssignedRole == role)
            {
                networkPlayer = candidate;
                return true;
            }
        }

        networkPlayer = null;
        return false;
    }

[Networked] public NetworkBool IsActivationTriggerHeld { get; private set; }
    [Networked] public NetworkBool AreCooperativeHandsContacted { get; private set; }
    [Networked] public NetworkBool IsRightTriggerHeld { get; private set; }
    [Networked] public NetworkBool IsLeftTriggerHeld { get; private set; }
    [Networked] public Hand CooperativeContactHand { get; private set; }
    [Networked] public NetworkBool HasCooperativeActivationSucceeded { get; private set; }
    [Networked] public float CooperativeHoldProgress { get; private set; }

    public bool IsTriggerHeld(Hand hand) =>
        hand == Hand.Right ? (bool)IsRightTriggerHeld :
        hand == Hand.Left ? (bool)IsLeftTriggerHeld : false;

    public Transform GetHand(Hand hand) =>
        hand == Hand.Right ? RightHand :
        hand == Hand.Left ? LeftHand : null;

    private TeleportGhostManager.CharacterType LocalCharacterType
    {
        get
        {
            if (Object.InputAuthority.PlayerId == 1)
            {
                return TeleportGhostManager.CharacterType.Mental;
            }

            return TeleportGhostManager.CharacterType.Ear;
        }
    }

    [Header("Network Transforms")]
    [SerializeField] private NetworkTransform playerTransform;
    [SerializeField] private NetworkTransform headTransform;
    [SerializeField] private NetworkTransform leftHandTransform;
    [SerializeField] private NetworkTransform rightHandTransform;

    [Header("Avatar")]
    [SerializeField] private GameObject baseAvatar;

    [SerializeField]
    private Transform sourceRoot;

    private HardwareRig hardwareRig;
    private TeleportGhostManager teleportGhostManager;

    public override void Spawned()
    {
        base.Spawned();

        // 모든 클라이언트에서 등록(호스트는 원격 플레이어의 손도 참조해야 한다).
        Players[Object.InputAuthority] = this;

        // 로컬(내) 플레이어 캐시 — 미션 결과를 Host 권한으로 브로드캐스트할 때 사용
        if (IsLocalNetworkRig)
            LocalInstance = this;


        if (IsLocalNetworkRig)
        {
                        // StartScene의 Host/Join 버튼으로 정해둔 로컬 Role을
            // 서버가 확정한 값으로 다시 맞춘다.
            RoleManager.SetLocalRole(AssignedRole);

hardwareRig = FindFirstObjectByType<HardwareRig>();

            if (hardwareRig == null)
            {
                Debug.LogError("HardwareRig를 찾을 수 없습니다.");
                return;
            }

            // 내 아바타 캡슐 콜라이더와 내 하드웨어 리그의 CharacterController가
            // 서로 밀어내며 하늘로 떠오르는 자가 충돌을 방지한다.
            var rigCharacterController = hardwareRig.GetComponent<CharacterController>();
            var myAvatarCollider = GetComponent<Collider>();
            if (rigCharacterController != null && myAvatarCollider != null)
                Physics.IgnoreCollision(myAvatarCollider, rigCharacterController, true);

            teleportGhostManager =
                FindFirstObjectByType<TeleportGhostManager>();

            if (teleportGhostManager == null)
            {
                Debug.LogError("TeleportGhostManager를 찾을 수 없습니다.");
                return;
            }

            teleportGhostManager.Initialize(
            LocalCharacterType,
            sourceRoot,
            hardwareRig.teleportInteractor
        );

            // 내 몸의 렌더러만 숨기기
            SetAvatarVisible(false);
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        if (LocalInstance == this)
            LocalInstance = null;

        if (Players.TryGetValue(Object.InputAuthority, out var np) && np == this)
            Players.Remove(Object.InputAuthority);
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (GetInput<RigState>(out var input))
        {
            if (HasNetworkStateAuthority)
            {
                IsRightTriggerHeld = input.RightTriggerPressed;
                IsLeftTriggerHeld = input.LeftTriggerPressed;
            }

            playerTransform.transform.SetPositionAndRotation(
                input.PlayerPosition,
                input.PlayerRotation);

            headTransform.transform.SetPositionAndRotation(
                input.HeadsetPosition,
                input.HeadsetRotation);

            leftHandTransform.transform.SetPositionAndRotation(
                input.LeftHandPosition,
                input.LeftHandRotation);

            rightHandTransform.transform.SetPositionAndRotation(
                input.RightHandPosition,
                input.RightHandRotation);
        }
    }

    public void SetCooperativeContactHand(Hand hand)
    {
        if (HasNetworkStateAuthority)
            CooperativeContactHand = hand;
    }

    public void SetCooperativeActivationSucceeded()
    {
        if (HasNetworkStateAuthority)
            HasCooperativeActivationSucceeded = true;
    }

    public void SetCooperativeHoldProgress(float progress)
    {
        if (HasNetworkStateAuthority)
            CooperativeHoldProgress = progress;
    }

    /// <summary>
    /// 공명 성공 상태를 되돌린다(거리 이탈로 제약 복귀 시).
    /// </summary>
    public void ClearCooperativeActivation()
    {
        if (!HasNetworkStateAuthority)
            return;

        HasCooperativeActivationSucceeded = false;
        CooperativeContactHand = Hand.None;
        CooperativeHoldProgress = 0f;
    }

    public override void Render()
    {
        base.Render();

        if (!IsLocalNetworkRig || hardwareRig == null)
            return;

        playerTransform.transform.SetPositionAndRotation(
            hardwareRig.playerTransform.position,
            hardwareRig.playerTransform.rotation);

        headTransform.transform.SetPositionAndRotation(
            hardwareRig.headTransform.position,
            hardwareRig.headTransform.rotation);

        leftHandTransform.transform.SetPositionAndRotation(
            hardwareRig.leftHandTransform.position,
            hardwareRig.leftHandTransform.rotation);

        rightHandTransform.transform.SetPositionAndRotation(
            hardwareRig.rightHandTransform.position,
            hardwareRig.rightHandTransform.rotation);
    }

    private void SetAvatarVisible(bool visible)
    {
        if (baseAvatar == null)
            return;

        Renderer[] renderers = baseAvatar.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer avatarRenderer in renderers)
        {
            avatarRenderer.enabled = visible;
        }
    }


    /// <summary>
    /// (Host 전용) 지정 플랜트의 색 복원을 두 플레이어 모두에게 브로드캐스트한다.
    /// Host 는 상태 권한을 가지므로 RPC 를 보낼 수 있고, InvokeLocal 로 자기 자신도 실행된다.
    /// </summary>
    public void RequestRevivePlant(int plantId)
    {
        if (HasNetworkStateAuthority)
            Rpc_RevivePlant(plantId);
        else
            PetalRhythmMission.ReviveById(plantId);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_RevivePlant(int plantId)
    {
        PetalRhythmMission.ReviveById(plantId);
    }
}
