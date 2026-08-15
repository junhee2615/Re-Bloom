using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using static Unity.Collections.Unicode;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class HardwareRig : MonoBehaviour, INetworkRunnerCallbacks
{
    public Transform playerTransform;

    public Transform headTransform;

    public Transform leftHandTransform;

    public Transform rightHandTransform;

    [Header("Teleport Ghost")]
    public XRRayInteractor teleportInteractor;

    private void OnEnable()
    {
        StartCoroutine(Register());
    }

    private IEnumerator Register()
    {
        while (NetworkManager.Instance == null || NetworkManager.Instance.Runner == null)
        {
            yield return null;
        }

        NetworkManager.Instance.Runner.AddCallbacks(this);
    }

    public void SetTrainParent(Transform train)
    {
        transform.SetParent(train, true);
    }

    public void ClearTrainParent()
    {
        transform.SetParent(null, true);
    }


    #region INetworkRunnerCallbacks
    void INetworkRunnerCallbacks.OnInput(NetworkRunner runner, NetworkInput input)
    {
        if (playerTransform == null ||
        headTransform == null ||
        leftHandTransform == null ||
        rightHandTransform == null)
        {
            return;
        }

        RigState xrRigState = new RigState();

        xrRigState.HeadsetPosition = headTransform.position;
        xrRigState.HeadsetRotation = headTransform.rotation;

        xrRigState.PlayerPosition = playerTransform.position;
        xrRigState.PlayerRotation = playerTransform.rotation;

        xrRigState.LeftHandPosition = leftHandTransform.position;
        xrRigState.LeftHandRotation = leftHandTransform.rotation;

        xrRigState.RightHandPosition = rightHandTransform.position;
        xrRigState.RightHandRotation = rightHandTransform.rotation;

        InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        rightController.TryGetFeatureValue(
            CommonUsages.triggerButton,
            out bool rightTriggerPressed
        );
        xrRigState.RightTriggerPressed = rightTriggerPressed;

        InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        leftController.TryGetFeatureValue(
            CommonUsages.triggerButton,
            out bool leftTriggerPressed
        );
        xrRigState.LeftTriggerPressed = leftTriggerPressed;

        leftController.TryGetFeatureValue(
            CommonUsages.primary2DAxis,
            out Vector2 moveValue
        );
        xrRigState.IsWalking = moveValue.magnitude > 0.1f;

        input.Set(xrRigState);
    }
    #endregion

    #region Unused INetworkRunnerCallbacks
    public void OnConnectedToServer(NetworkRunner runner)
    {

    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {

    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {

    }

    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {

    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {

    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {

    }


    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {

    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {

    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {

    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {

    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {

    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {

    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {

    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        //if (SceneManager.GetActiveScene().buildIndex != 2)
        //    return;

        StartCoroutine(MoveToSpawnPoint(runner));
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {

    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {

    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {

    }

    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {

    }
    #endregion

    private IEnumerator MoveToSpawnPoint(NetworkRunner runner)
    {
        yield return null;

        Transform spawnPoint;

        if (runner.LocalPlayer.PlayerId == 1)
            spawnPoint = GameObject.Find("HostSpawnPoint").transform;
        else
            spawnPoint = GameObject.Find("ClientSpawnPoint").transform;

        TeleportTo(spawnPoint);
    }

    public void TeleportTo(Transform target)
    {
        Vector3 headOffset = headTransform.position - playerTransform.position;
        headOffset.y = 0f;

        playerTransform.position = target.position - headOffset;
        playerTransform.rotation = target.rotation;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Instance != null &&
            NetworkManager.Instance.Runner != null)
        {
            NetworkManager.Instance.Runner.RemoveCallbacks(this);
        }
    }
}

public struct RigState : INetworkInput
{
    public Vector3 PlayerPosition;
    public Quaternion PlayerRotation;

    public Vector3 HeadsetPosition;
    public Quaternion HeadsetRotation;

    public Vector3 LeftHandPosition;
    public Quaternion LeftHandRotation;

    public Vector3 RightHandPosition;
    public Quaternion RightHandRotation;
    public NetworkBool RightTriggerPressed;
    public NetworkBool LeftTriggerPressed;

    public NetworkBool IsWalking;
}


