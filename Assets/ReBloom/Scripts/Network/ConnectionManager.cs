using UnityEngine;
using UnityEngine.UI;


public class ConnectionManager : MonoBehaviour
{
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

    private async void Enter(string roomCode, SessionMode mode)
    {
        if (NetworkManager.Instance == null)
        {
            Debug.LogError("NetworkManager를 찾을 수 없습니다. StartScene의 Manager 오브젝트를 확인하세요.", this);
            return;
        }

        SetEntryButtonsInteractable(false);

        bool entered = await NetworkManager.Instance.EnterSession(roomCode, mode);

        // 성공하면 곧바로 Lobby 씬으로 넘어가면서 이 오브젝트는 사라진다.
        // 실패했을 때만 버튼을 다시 열어 준다.
        if (!entered)
            SetEntryButtonsInteractable(true);
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
