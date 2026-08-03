using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class NetworkPlayer : NetworkBehaviour
{
    // 내 플레이어인지 확인
    public bool IsLocalNetworkRig => Object != null && Object.HasInputAuthority;
    public HardwareRig HardwareRig => hardwareRig;
    public Transform PlayerTransform => playerTransform != null ? playerTransform.transform : null;

    // PlayerRef → NetworkPlayer 레지스트리.
    // 호스트가 "어떤 플레이어가 잡았는지"만 알아도 그 플레이어의 손 트랜스폼을
    // 직접 참조할 수 있도록 모든 인스턴스를 등록해 둔다. (WaterMissionObstacle 등에서 사용)
    private static readonly Dictionary<PlayerRef, NetworkPlayer> _players =
        new Dictionary<PlayerRef, NetworkPlayer>();

    public static bool TryGet(PlayerRef player, out NetworkPlayer networkPlayer)
        => _players.TryGetValue(player, out networkPlayer);

    public Transform LeftHand => leftHandTransform != null ? leftHandTransform.transform : null;
    public Transform RightHand => rightHandTransform != null ? rightHandTransform.transform : null;
    public Transform RightHandTransform => rightHandTransform != null ? rightHandTransform.transform : null;
    public bool HasNetworkStateAuthority => Object != null && Object.IsValid && Object.HasStateAuthority;

    [Networked] public NetworkBool IsActivationTriggerHeld { get; private set; }
    [Networked] public NetworkBool AreCooperativeHandsContacted { get; private set; }
    [Networked] public NetworkBool HasCooperativeActivationSucceeded { get; private set; }

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
        _players[Object.InputAuthority] = this;

        if (IsLocalNetworkRig)
        {
            hardwareRig = FindFirstObjectByType<HardwareRig>();

            if (hardwareRig == null)
            {
                Debug.LogError("HardwareRig를 찾을 수 없습니다.");
                return;
            }

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

        if (_players.TryGetValue(Object.InputAuthority, out var np) && np == this)
            _players.Remove(Object.InputAuthority);
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (GetInput<RigState>(out var input))
        {
            if (HasNetworkStateAuthority)
                IsActivationTriggerHeld = input.RightTriggerPressed;

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

    public void SetCooperativeHandsContacted(bool contacted)
    {
        if (HasNetworkStateAuthority)
            AreCooperativeHandsContacted = contacted;
    }

    public void SetCooperativeActivationSucceeded()
    {
        if (HasNetworkStateAuthority)
            HasCooperativeActivationSucceeded = true;
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
}
