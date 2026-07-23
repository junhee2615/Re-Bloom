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

    [SerializeField, Tooltip("Host가 세션 시작 시 로드할 씬 이름. Build Settings에 등록된 씬이어야 함.")]
    private string _targetSceneName = "Stage1";

    public NetworkRunner Runner { get; private set; }

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

    public async void CreateSession(string roomCode)
    {
        if (_sessionStarted) return;
        _sessionStarted = true;

        NetworkSceneManagerDefault sceneManager = GetComponent<NetworkSceneManagerDefault>();
        if (!GetTargetScene(sceneManager, out SceneRef targetScene))
        {
            _sessionStarted = false;
            return;
        }

        CreateRunner();

        var args = new StartGameArgs()
        {
            GameMode = GameMode.Host,
            SessionName = roomCode,
            SceneManager = sceneManager,
            Scene = targetScene
        };
        await Runner.StartGame(args);
    }

    public async void JoinSession(string roomCode)
    {
        if (_sessionStarted) return;
        _sessionStarted = true;
        CreateRunner();

        var args = new StartGameArgs()
        {
            GameMode = GameMode.Client,
            SessionName = roomCode,
            SceneManager = GetComponent<NetworkSceneManagerDefault>()
        };
        await Runner.StartGame(args);
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
        if (!GetTargetScene(sceneManager, out SceneRef targetScene))
            return;

        var args = new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = SessionName,
            SceneManager = sceneManager,
            Scene = targetScene

        };
        await Runner.StartGame(args);
    }

    private bool GetTargetScene(NetworkSceneManagerDefault sceneManager, out SceneRef targetScene)
    {
        targetScene = SceneRef.None;

        if (sceneManager == null)
        {
            Debug.LogError("NetworkSceneManagerDefault가 없어 네트워크 씬을 로드할 수 없습니다.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_targetSceneName))
        {
            Debug.LogError("Target Scene Name이 비어 있습니다.", this);
            return false;
        }

        targetScene = sceneManager.GetSceneRef(_targetSceneName.Trim());
        if (targetScene == SceneRef.None)
        {
            Debug.LogError(
                $"씬 '{_targetSceneName}'을 찾을 수 없습니다. File > Build Profiles > Scene List에 씬을 등록했는지 확인하세요.",
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
