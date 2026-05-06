using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

public class NetworkRig : NetworkBehaviour
{
    public bool IsLocalNetworkRig => Object.HasInputAuthority;

    [Header("RigComponents")]
    [SerializeField]
    private NetworkTransform playerTransform;

    [SerializeField]
    private NetworkTransform headTransform;

    [SerializeField]
    private NetworkTransform leftHandTransform;

    [SerializeField]
    private NetworkTransform rightHandTransform;

    HardwareRig hardwareRig;

    public override void Spawned()
    {
        base.Spawned();

        if (IsLocalNetworkRig)
        {
            hardwareRig = FindObjectOfType<HardwareRig>();

            if (hardwareRig == null)
                Debug.LogError("Missing HardwareRig in the scene");

            var renderer = GetComponentInChildren<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;
        }
        // else it means that this is a client
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        // 서버(권한 가진 쪽)가 모든 플레이어 위치 적용
        if (Object.HasStateAuthority)
        {
            if (GetInput<RigState>(out var input))
            {
                playerTransform.transform.SetPositionAndRotation(input.PlayerPosition, input.PlayerRotation);

                headTransform.transform.SetPositionAndRotation(input.HeadsetPosition, input.HeadsetRotation);

                leftHandTransform.transform.SetPositionAndRotation(input.LeftHandPosition, input.LeftHandRotation);

                rightHandTransform.transform.SetPositionAndRotation(input.RightHandPosition, input.RightHandRotation);
            }
        }
    }

    public override void Render()
    {
        base.Render();
        if (IsLocalNetworkRig)
        {
            playerTransform.transform.SetPositionAndRotation(hardwareRig.playerTransform.position, hardwareRig.playerTransform.rotation);

            headTransform.transform.SetPositionAndRotation(hardwareRig.headTransform.position, hardwareRig.headTransform.rotation);

            leftHandTransform.transform.SetPositionAndRotation(hardwareRig.leftHandTransform.position, hardwareRig.leftHandTransform.rotation);

            rightHandTransform.transform.SetPositionAndRotation(hardwareRig.rightHandTransform.position, hardwareRig.rightHandTransform.rotation);

        }
    }
}