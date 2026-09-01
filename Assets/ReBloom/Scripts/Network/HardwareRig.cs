using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using static Unity.Collections.Unicode;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
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


    // =================================================
    // Locomotion Lock
    //
    // 연출(열차 출발 등) 중 이동/회전/텔레포트를 잠근다.
    // 리그는 씬을 넘어가도 유지되므로, 잠금 해제를 연출 스크립트의
    // 수명에 맡기면 다음 씬까지 잠긴 채로 넘어갈 수 있다.
    // 그래서 잠금 상태는 리그가 직접 들고 있고,
    // OnSceneLoadDone에서 무조건 해제한다.
    // =================================================

    private readonly List<LocomotionProvider> lockedProviders =
        new List<LocomotionProvider>();

    private readonly List<GameObject> lockedTeleportInteractors =
        new List<GameObject>();

    public bool IsLocomotionLocked { get; private set; }

    /// <summary>
    /// 이동/회전/텔레포트를 잠그거나 푼다. 중력은 항상 유지한다.
    /// 잠글 때 실제로 켜져 있던 대상만 기억했다가 그대로 되돌리므로,
    /// 원래 꺼져 있던 프로바이더를 임의로 켜지 않는다.
    /// </summary>
    public void SetLocomotionLocked(bool locked)
    {
        if (locked == IsLocomotionLocked)
            return;

        if (locked)
        {
            lockedProviders.Clear();
            lockedTeleportInteractors.Clear();

            foreach (LocomotionProvider provider in
                     GetComponentsInChildren<LocomotionProvider>(true))
            {
                if (provider is GravityProvider)
                    continue;

                if (!provider.enabled)
                    continue;

                provider.enabled = false;
                lockedProviders.Add(provider);
            }

            foreach (XRRayInteractor interactor in
                     GetComponentsInChildren<XRRayInteractor>(true))
            {
                GameObject interactorObject = interactor.gameObject;

                if (!interactorObject.name.Contains("Teleport"))
                    continue;

                if (!interactorObject.activeSelf)
                    continue;

                interactorObject.SetActive(false);
                lockedTeleportInteractors.Add(interactorObject);
            }
        }
        else
        {
            foreach (LocomotionProvider provider in lockedProviders)
            {
                if (provider != null)
                    provider.enabled = true;
            }

            foreach (GameObject interactorObject in lockedTeleportInteractors)
            {
                if (interactorObject != null)
                    interactorObject.SetActive(true);
            }

            lockedProviders.Clear();
            lockedTeleportInteractors.Clear();
        }

        IsLocomotionLocked = locked;
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

        // 이전 씬의 연출이 이동을 잠근 채로 끝났더라도
        // 새 씬에서는 반드시 풀린 상태로 시작한다.
        SetLocomotionLocked(false);

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

        // Lobby에서 고른 역할을 따른다.
        // 씬에 역할 이름의 스폰 포인트가 없으면 기존 Host/Client 이름으로 넘어간다.
        bool isMental = RoleManager.LocalIsMental;
        string roleName = isMental ? "MentalSpawnPoint" : "EarSpawnPoint";
        string legacyName = isMental ? "HostSpawnPoint" : "ClientSpawnPoint";

        GameObject spawnPointObject = GameObject.Find(roleName);
        if (spawnPointObject == null)
            spawnPointObject = GameObject.Find(legacyName);

        // Lobby처럼 스폰 포인트가 없는 씬에서는 배치된 위치를 그대로 쓴다.
        if (spawnPointObject == null)
        {
            Debug.Log($"[HardwareRig] '{roleName}' / '{legacyName}' 둘 다 없어 현재 위치를 유지합니다.");
            yield break;
        }

        TeleportTo(spawnPointObject.transform);
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


