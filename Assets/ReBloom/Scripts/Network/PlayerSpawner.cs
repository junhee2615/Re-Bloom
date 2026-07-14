using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;

public class PlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private NetworkPrefabRef playerEarPrefab; // 청각 제약 캐릭터
    [SerializeField]
    private NetworkPrefabRef playerMentalPrefab; // 정신 제약 캐릭터

    // Dictionary of spawned user prefabs, to destroy them on disconnection
    private Dictionary<PlayerRef, NetworkObject> _spawnedUsers = new Dictionary<PlayerRef, NetworkObject>();

    private void OnEnable()
    {
        StartCoroutine(RegisterCallback());
    }

    private IEnumerator RegisterCallback()
    {
        while (NetworkManager.Instance == null || NetworkManager.Instance.Runner == null)
        {
            yield return null;
        }

        NetworkManager.Instance.Runner.AddCallbacks(this);
    }

    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        // Host 스폰
        if (runner.IsServer)
        {

            // Host가 PlayerId 1, Client가 PlayerId 2
            SpawnPlayer(runner, player);
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedUsers.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedUsers.Remove(player);
        }
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedUsers.ContainsKey(player))
            return;

        NetworkPrefabRef prefabToSpawn =
            (player.PlayerId == 1) ? playerMentalPrefab : playerEarPrefab;

        NetworkObject networkPlayerObject =
            runner.Spawn(prefabToSpawn, Vector3.zero, Quaternion.identity, player);

        _spawnedUsers.Add(player, networkPlayerObject);
    }
    #endregion

    #region Unsed INetworkRunnerCallbacks
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

    public void OnInput(NetworkRunner runner, NetworkInput input)
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



    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {

    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data)
    {

    }

    public void OnSceneLoadDone(NetworkRunner runner)
    {
        // After a scene transition the previous avatars are destroyed, leaving
        // null entries in the dictionary. Prune only those, then spawn for any
        // active player that has no live avatar. Never despawn/clear live
        // entries (doing so caused duplicate spawns).
        if (!runner.IsServer)
            return;

        var stale = new List<PlayerRef>();
        foreach (var kv in _spawnedUsers)
            if (kv.Value == null)
                stale.Add(kv.Key);
        foreach (var p in stale)
            _spawnedUsers.Remove(p);

        foreach (var player in runner.ActivePlayers)
            SpawnPlayer(runner, player);
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

}