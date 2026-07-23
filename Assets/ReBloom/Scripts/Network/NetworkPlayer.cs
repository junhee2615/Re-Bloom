using Fusion;
using UnityEngine;

public class NetworkPlayer : NetworkBehaviour
{
    // 내 플레이어인지 확인
    public bool IsLocalNetworkRig => Object.HasInputAuthority;
    public HardwareRig HardwareRig => hardwareRig;
    public Transform PlayerTransform => playerTransform.transform;

    private TeleportGhostManager.CharacterType LocalCharacterType
    {
        get
        {
            if (Object.InputAuthority.PlayerId == 1)
            {
                return TeleportGhostManager.CharacterType.Ear;
            }

            return TeleportGhostManager.CharacterType.Mental;
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

            // 내 몸 숨기기
            if (baseAvatar != null)
            {
                baseAvatar.SetActive(false);
            }
        }
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (GetInput<RigState>(out var input))
        {
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
}