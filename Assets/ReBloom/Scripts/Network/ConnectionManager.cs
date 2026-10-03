using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


public class ConnectionManager : MonoBehaviour
{
    /// <summary>버튼을 누른 뒤 곧바로 로드할 오프닝 씬. Build Profiles > Scene List에 등록되어 있어야 한다.</summary>
    private const string OpeningSceneName = "Opening";

    [SerializeField, Tooltip("Multi(2인 협동) 입장 시 사용할 세션 이름. 같은 이름끼리 매칭된다.")]
    private string multiRoomCode = "TestRoom";

    [SerializeField, Tooltip("Single(개인 테스트) 입장 시 세션 이름 앞에 붙는 접두사. 뒤에 기기 고유 ID가 붙어 항상 혼자만의 방이 된다.")]
    private string singleRoomPrefix = "Solo_";

    [SerializeField, Tooltip("입장 처리 중 잠글 버튼들. 비워 두면 잠그지 않는다.")]
    private Button[] entryButtons;

    /// <summary>StartScene의 MultiBtn.OnClick에 연결한다.</summary>
    public void EnterMulti()
    {
        Enter(multiRoomCode, SessionMode.Multi);
    }

    /// <summary>StartScene의 SingleBtn.OnClick에 연결한다.</summary>
    public void EnterSingle()
    {
        // 기기 고유 ID를 붙여 다른 테스터와 같은 방에 들어가는 사고를 막는다.
        Enter(singleRoomPrefix + SystemInfo.deviceUniqueIdentifier, SessionMode.Single);
    }

    /// <summary>
    /// 버튼을 누르면 선택값만 보관하고 Opening으로 넘어간다.
    ///
    /// 여기서 Fusion 세션을 시작하지 않는 이유:
    /// Opening은 Stage1/Stage2를 일반 SceneManager로 Additive Load해 잠깐 보여 주는
    /// 순수 로컬 연출이다. Runner가 살아 있는 상태에서 그렇게 하면
    /// PlayerSpawner가 Active Scene 이름으로 스폰 여부를 판정하기 때문에
    /// 임시 Stage에 플레이어 아바타가 스폰되는 등 세션 상태가 오염된다.
    /// 또 멀티에서 두 피어의 45초 연출을 동기화해야 하는 문제가 생긴다.
    ///
    /// 그래서 EnterSession은 Opening이 끝난 뒤에 호출한다.
    /// 세션의 첫 Network Scene은 계속 Lobby이고, NetworkManager는 수정하지 않는다.
    ///
    /// NetworkManager는 Awake에서 DontDestroyOnLoad되므로 Opening으로 넘어가도 살아남는다.
    /// 다음 단계에서 Opening이 OpeningSessionRequest를 읽어 EnterSession을 호출한다.
    /// </summary>
    private void Enter(string roomCode, SessionMode mode)
    {
        if (NetworkManager.Instance == null)
        {
            Debug.LogError("NetworkManager를 찾을 수 없습니다. StartScene의 Manager 오브젝트를 확인하세요.", this);
            return;
        }

        // 연출 중 버튼이 다시 눌리는 사고를 막는다. 이 씬은 곧 언로드된다.
        SetEntryButtonsInteractable(false);

        OpeningSessionRequest.Set(roomCode, mode);

        Debug.Log($"[Opening] 세션 요청 보관 - room={roomCode}, mode={mode}. '{OpeningSceneName}'을 로드합니다.", this);

        SceneManager.LoadScene(OpeningSceneName, LoadSceneMode.Single);
    }

    private void SetEntryButtonsInteractable(bool value)
    {
        if (entryButtons == null)
            return;

        foreach (Button button in entryButtons)
        {
            if (button != null)
                button.interactable = value;
        }
    }
}
