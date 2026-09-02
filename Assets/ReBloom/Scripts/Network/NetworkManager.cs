using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    //creating a singleton
    public static NetworkManager Instance { get; private set; }

    [SerializeField]
    private GameObject _runnerPrefab;

    /// <summary>
    /// 세션 입장 직후 항상 로드하는 씬. StartScene 다음은 언제나 Lobby다.
    /// (Host가 로드하면 Client는 Fusion이 자동으로 따라온다.)
    /// Build Profiles > Scene List에 등록되어 있어야 한다.
    /// 어느 스테이지로 갈지는 Lobby의 LobbyManager.nextSceneName에서 정한다.
    /// </summary>
    public const string LobbySceneName = "Lobby";

    public NetworkRunner Runner { get; private set; }

    /// <summary>StartScene에서 어느 버튼으로 입장했는지(Multi / Single).</summary>
    public SessionMode Mode { get; private set; } = SessionMode.Multi;

    /// <summary>세션 시작 절차가 이미 진행되었는지.</summary>
    public bool IsSessionStarted => _sessionStarted;

    private bool _sessionStarted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    private void Start()
    {
        // fixing the server to a perticular region
        Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.FixedRegion = "asia";
    }

    /// <summary>
    /// StartScene의 Multi / Single 버튼 공용 진입점.
    ///
    /// GameMode.AutoHostOrClient 이므로 같은 이름의 방이 없으면 Host,
    /// 이미 있으면 Client가 된다. Host가 되면 Lobby 씬을 로드하고
    /// Client는 Fusion이 자동으로 같은 씬으로 따라온다.
    /// </summary>
    /// <returns>세션 입장 성공 여부.</returns>
    public async Task<bool> EnterSession(string roomCode, SessionMode mode)
    {
        if (_sessionStarted)
            return false;

        _sessionStarted = true;
        Mode = mode;

        NetworkSceneManagerDefault sceneManager = GetComponent<NetworkSceneManagerDefault>();
        if (!GetLobbyScene(sceneManager, out SceneRef lobbyScene))
        {
            _sessionStarted = false;
            return false;
        }

        CreateRunner();

        var args = new StartGameArgs()
        {
            GameMode = GameMode.AutoHostOrClient,
            SessionName = roomCode,
            SceneManager = sceneManager,
            Scene = lobbyScene
        };

        StartGameResult result = await Runner.StartGame(args);

        if (!result.Ok)
        {
            Debug.LogError($"세션 입장 실패 (room={roomCode}): {result.ShutdownReason} / {result.ErrorMessage}", this);

            if (Runner != null)
            {
                Destroy(Runner.gameObject);
                Runner = null;
            }

            _sessionStarted = false;
            return false;
        }

        Debug.Log($"세션 입장 성공 - room={roomCode}, mode={Mode}, isHost={Runner.IsServer}, playerId={Runner.LocalPlayer.PlayerId}");
        return true;
    }

    public void CreateRunner()
    {
        Runner = Instantiate(_runnerPrefab, transform).GetComponent<NetworkRunner>();
        Runner.AddCallbacks(this);
        Runner.ProvideInput = true;
    }

    private async Task Connect(string SessionName)
    {
        NetworkSceneManagerDefault sceneManager = GetComponent<NetworkSceneManagerDefault>();
        if (!GetLobbyScene(sceneManager, out SceneRef lobbyScene))
            return;

        var args = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = SessionName,
            SceneManager = sceneManager,
            Scene = lobbyScene

        };
        await Runner.StartGame(args);
    }

    /// <summary>씬 이름을 Fusion의 SceneRef로 바꾼다. 못 찾으면 SceneRef.None.</summary>
    public SceneRef GetSceneRef(string sceneName)
    {
        NetworkSceneManagerDefault sceneManager = GetComponent<NetworkSceneManagerDefault>();

        if (sceneManager == null || string.IsNullOrWhiteSpace(sceneName))
            return SceneRef.None;

        return sceneManager.GetSceneRef(sceneName.Trim());
    }

    /// <summary>세션 입장 시 로드할 Lobby 씬을 찾는다.</summary>
    private bool GetLobbyScene(NetworkSceneManagerDefault sceneManager, out SceneRef lobbyScene)
    {
        lobbyScene = SceneRef.None;

        if (sceneManager == null)
        {
            Debug.LogError("NetworkSceneManagerDefault가 없어 네트워크 씬을 로드할 수 없습니다.", this);
            return false;
        }

        lobbyScene = sceneManager.GetSceneRef(LobbySceneName);

        if (lobbyScene == SceneRef.None)
        {
            Debug.LogError(
                $"씬 '{LobbySceneName}'을 찾을 수 없습니다. File > Build Profiles > Scene List에 씬을 등록했는지 확인하세요.",
                this);
            return false;
        }

        return true;
    }

    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log("<<<<<<<< A new player joined to the session >>>>>>>");
        Debug.Log("<<<<<<< IsMasterClient >>>>>>>>" + player.IsMasterClient);
        Debug.Log("<<<<<<< PlayerID >>>>>>>>" + player.PlayerId);
        Debug.Log("<<<<<<< IsRealPlayer >>>>>>>>" + player.IsRealPlayer);
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log("<<<<<<<< A player left the session >>>>>>>");
        Debug.Log("<<<<<<< IsMasterClient >>>>>>>>" + player.IsMasterClient);
        Debug.Log("<<<<<<< PlayerID >>>>>>>>" + player.PlayerId);
        Debug.Log("<<<<<<< IsRealPlayer >>>>>>>>" + player.IsRealPlayer);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log("<<<<<<< Runner Shutdown >>>>>>>>");

        // 다시 StartScene으로 돌아왔을 때 재입장이 가능하도록 상태를 되돌린다.
        _sessionStarted = false;
        Runner = null;

        // 세션이 끝난 뒤에 직전 Role이 남아 오판하지 않도록 초기화한다.
        RoleManager.ClearLocalRole();
    }
    #endregion

    #region INetworkRunnerCallbacks (Unused)
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
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
    }


    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message)
    {
    }
    #endregion

}
