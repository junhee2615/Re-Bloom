using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;

public class PlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField, Tooltip("ear 역할 캐릭터 프리팹.")]
    private NetworkPrefabRef playerEarPrefab;

    [SerializeField, Tooltip("mental 역할 캐릭터 프리팹.")]
    private NetworkPrefabRef playerMentalPrefab;

    [SerializeField, Tooltip("플레이어 아바타를 스폰하지 않을 씬 이름. 역할이 정해지기 전 씬(Lobby 등)을 넣는다.")]
    private string[] noSpawnScenes = { "StartScene", "Lobby" };

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
        if (!runner.IsServer)
            return;

        // Lobby처럼 역할이 정해지기 전 씬에서는 아바타를 만들지 않는다.
        if (!IsSpawnAllowedScene())
            return;

        SpawnPlayer(runner, player);
    }

    private void SpawnPlayer(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedUsers.ContainsKey(player))
            return;

        // Lobby에서 고른 역할을 그대로 쓴다.
        // Lobby를 거치지 않고 Stage 씬을 직접 실행한 경우에만 기존 PlayerId 규칙으로 넘어간다.
        Role role;
        if (!RoleAssignments.TryGet(player, out role))
        {
            role = (player.PlayerId == 1) ? Role.mental : Role.ear;
            Debug.LogWarning($"[PlayerSpawner] player {player.PlayerId}의 Lobby 선택이 없어 PlayerId 규칙({role})으로 스폰합니다.", this);
        }

        NetworkPrefabRef prefabToSpawn =
            (role == Role.mental) ? playerMentalPrefab : playerEarPrefab;

        NetworkObject networkPlayerObject =
            runner.Spawn(prefabToSpawn, Vector3.zero, Quaternion.identity, player,
                onBeforeSpawned: (NetworkRunner spawnRunner, NetworkObject spawnedObject) =>
                {
                    // Spawned()가 불리기 전에 Role을 넣어야
                    // 모든 클라이언트가 처음부터 올바른 값을 본다.
                    var networkPlayer = spawnedObject.GetComponent<NetworkPlayer>();
                    if (networkPlayer == null)
                        networkPlayer = spawnedObject.GetComponentInChildren<NetworkPlayer>(true);

                    if (networkPlayer != null)
                        networkPlayer.AssignRole(role);
                    else
                        Debug.LogError("스폰된 플레이어 프리팹에 NetworkPlayer가 없어 Role을 부여하지 못했습니다.", this);
                });

        _spawnedUsers.Add(player, networkPlayerObject);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedUsers.TryGetValue(player, out NetworkObject networkObject))
        {
            runner.Despawn(networkObject);
            _spawnedUsers.Remove(player);
        }
    }
    #endregion

    private static string CurrentSceneName =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

    /// <summary>현재 씬에서 플레이어 아바타를 스폰해도 되는지.</summary>
    private bool IsSpawnAllowedScene()
    {
        if (noSpawnScenes == null)
            return true;

        string current = CurrentSceneName;

        foreach (string sceneName in noSpawnScenes)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                continue;

            if (string.Equals(sceneName.Trim(), current, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

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
        // null entries. Prune only those, then spawn for any active player that
        // has no live avatar. Never despawn/clear live entries (that caused
        // duplicate spawns).
        if (!runner.IsServer)
            return;

        var stale = new List<PlayerRef>();
        foreach (var kv in _spawnedUsers)
            if (kv.Value == null)
                stale.Add(kv.Key);
        foreach (var p in stale)
            _spawnedUsers.Remove(p);

        // 역할 선택 전 씬(Lobby)에서는 서로의 아바타가 보이면 안 되므로 스폰을 건너뛴다.
        if (!IsSpawnAllowedScene())
        {
            Debug.Log($"[PlayerSpawner] '{CurrentSceneName}' 씬에서는 아바타를 스폰하지 않습니다.");
            return;
        }

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