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


    // player의 role 부여(Role 스크립트)
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
    [Networked] public NetworkBool IsWalking { get; private set; }

    public bool IsTriggerHeld(Hand hand) =>
        hand == Hand.Right ? (bool)IsRightTriggerHeld :
        hand == Hand.Left ? (bool)IsLeftTriggerHeld : false;

    public Transform GetHand(Hand hand) =>
        hand == Hand.Right ? RightHand :
        hand == Hand.Left ? LeftHand : null;

    /// <summary>
    /// 텔레포트 고스트에 쓸 캐릭터 종류.
    /// Lobby에서 고른 역할(AssignedRole)을 그대로 따른다.
    /// </summary>
    private TeleportGhostManager.CharacterType LocalCharacterType =>
        AssignedRole == Role.mental
            ? TeleportGhostManager.CharacterType.Mental
            : TeleportGhostManager.CharacterType.Ear;

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
                IsWalking = input.IsWalking;
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

    /// <summary>
    /// 이 플레이어의 아바타 렌더러를 켜거나 끈다.
    /// 로컬 플레이어는 Spawned에서 이미 숨겨져 있으므로,
    /// 외부(컷씬 등)에서는 원격 플레이어에만 사용할 것.
    /// </summary>
    public void SetAvatarVisible(bool visible)
    {
        if (baseAvatar == null)
            return;

        Renderer[] renderers = baseAvatar.GetComponentsInChildren<Renderer>(true);

        foreach (Renderer avatarRenderer in renderers)
        {
            avatarRenderer.enabled = visible;
        }
    }


    // 수생식물 재생
public void RequestRevivePlant(int plantId)
    {
        // mental이 Host든 Client든 상관없이 두 플레이어 모두에게 복원을 전파한다.
        if (HasNetworkStateAuthority)
            Rpc_RevivePlant(plantId);          // 권한자(Host) → 모두
        else
            Rpc_RequestRevivePlant(plantId);   // 클라이언트 → 권한자에게 요청 → 권한자가 모두에게 전파
    }

    // 클라이언트(비권한자)가 복원을 일으켰을 때, 권한자에게 전파를 요청한다.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_RequestRevivePlant(int plantId)
    {
        Rpc_RevivePlant(plantId);
    }

    /// <summary>클라이언트 → 권한자 중계. 권한자가 받아 전원에게 재방송한다.</summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_RequestRevivePlant(int plantId, RpcInfo info = default)
    {
        Rpc_RevivePlant(plantId);
    }


    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_RevivePlant(int plantId)
    {
        PetalRhythmMission.ReviveById(plantId);
    }

    // 씬 안에 NetworkObject 따로 두지 않고 플레이어 오브젝트를 통로로 씀
    /// <summary>로컬 플레이어가 이번 물결을 맞추었다고 호스트에 보고한다.</summary>
    public void RequestRiverbedHit(int waveIndex, int role)
    {
        if (HasNetworkStateAuthority)
            ReBloom.Water.RiverbedMissionNet.HostReceiveHit(waveIndex, (Role)role);
        else
            Rpc_RequestRiverbedHit(waveIndex, role);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void Rpc_RequestRiverbedHit(int waveIndex, int role, RpcInfo info = default)
    {
        ReBloom.Water.RiverbedMissionNet.HostReceiveHit(waveIndex, (Role)role);
    }

    // 물결 미션 시작 여부
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_RiverbedStart()
    {
        ReBloom.Water.RiverbedMissionNet.LocalStart();
    }

    // 진짜 물결인지 아닌지
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_RiverbedWave(int waveIndex, int isReal, float approachDuration)
    {
        ReBloom.Water.RiverbedMissionNet.LocalPlayWave(waveIndex, isReal != 0, approachDuration);
    }

    // 성공 횟수 세기(ear, mental 둘 다 클리어)
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_RiverbedSuccess(int successCount, float wetBaseline)
    {
        ReBloom.Water.RiverbedMissionNet.LocalApplySuccess(successCount, wetBaseline);
    }

    // 물결 미션 성공
    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void Rpc_RiverbedComplete()
    {
        ReBloom.Water.RiverbedMissionNet.LocalComplete();
    }

}
