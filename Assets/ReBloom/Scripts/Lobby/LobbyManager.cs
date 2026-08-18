using System.Collections;
using Fusion;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Lobby 씬의 역할 선택을 두 플레이어 사이에서 동기화한다.
///
/// - 선택 요청은 클라이언트 -> StateAuthority(Host) RPC로 보내고, 상태 변경은 Host만 한다.
/// - MentalOwner / EarOwner 가 [Networked] 라서 두 플레이어가 항상 같은 화면을 본다.
/// - 한 번 고르면 확정이다. 이미 정해진 역할 버튼과, 선택을 마친 플레이어의 버튼은 잠긴다.
/// - Multi 모드는 두 역할이 모두 정해지면, Single 모드는 하나만 정해져도
///   Host가 startDelaySeconds 뒤에 다음 씬을 로드한다.
///
/// Lobby 씬의 NetworkObject 가 붙은 GameObject에 올린다.
/// </summary>
public class LobbyManager : NetworkBehaviour
{
    public static LobbyManager Instance { get; private set; }

    [Header("역할 선택 버튼")]
    [SerializeField, Tooltip("mental 역할 선택 버튼.")]
    private Button mentalButton;

    [SerializeField, Tooltip("ear 역할 선택 버튼.")]
    private Button earButton;

    [Header("다음 씬")]
    [SerializeField, Tooltip("선택이 끝나면 이동할 씬 이름. Build Profiles > Scene List에 등록되어 있어야 한다.")]
    private string nextSceneName = "Stage1";

    [SerializeField, Tooltip("선택 완료 후 씬을 로드하기까지 기다릴 시간(초). 상대가 무엇을 골랐는지 볼 시간을 준다.")]
    private float startDelaySeconds = 2f;

    [Header("선택됨 표시")]
    [SerializeField, Tooltip("역할이 선택되면 버튼을 이 색으로 바꾼다. 두 플레이어 모두에게 똑같이 보인다.")]
    private Color takenColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [SerializeField, Tooltip("역할이 선택되면 버튼 라벨을 이 색으로 바꾼다.")]
    private Color takenTextColor = new Color(0.85f, 0.85f, 0.85f, 1f);

    [SerializeField, Tooltip("켜면 Unity 버튼의 Disabled 틴트를 지워, 위에서 지정한 색이 흐려지지 않고 그대로 보인다.")]
    private bool neutralizeDisabledTint = true;

    /// <summary>mental을 고른 플레이어. 아무도 안 골랐으면 PlayerRef.None.</summary>
    [Networked] public PlayerRef MentalOwner { get; set; }

    /// <summary>ear를 고른 플레이어. 아무도 안 골랐으면 PlayerRef.None.</summary>
    [Networked] public PlayerRef EarOwner { get; set; }

    private Coroutine _startRoutine;
    private RoleButtonVisual _mentalVisual;
    private RoleButtonVisual _earVisual;

    public override void Spawned()
    {
        base.Spawned();

        Instance = this;

        if (HasStateAuthority)
        {
            MentalOwner = PlayerRef.None;
            EarOwner = PlayerRef.None;
            RoleAssignments.Clear();
        }

        CacheButtonVisuals();
        RefreshButtons();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        if (Instance == this)
            Instance = null;
    }

    // ------------------------------------------------------------------
    // 버튼에서 호출 (UnityEvent)
    // ------------------------------------------------------------------

    /// <summary>Lobby 씬의 MentalBtn.OnClick에 연결한다.</summary>
    public void OnMentalButtonClicked()
    {
        RequestSelect(Role.mental);
    }

    /// <summary>Lobby 씬의 EarBtn.OnClick에 연결한다.</summary>
    public void OnEarButtonClicked()
    {
        RequestSelect(Role.ear);
    }

    private void RequestSelect(Role role)
    {
        if (Runner == null || Object == null || !Object.IsValid)
        {
            Debug.LogWarning("[LobbyManager] 아직 네트워크에 연결되지 않아 선택을 보낼 수 없습니다.", this);
            return;
        }

        Rpc_SelectRole(role);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_SelectRole(Role role, RpcInfo info = default)
    {
        PlayerRef sender = info.Source;

        // Host가 자기 버튼을 눌러 로컬로 실행된 경우 Source가 비어 있을 수 있다.
        if (sender == PlayerRef.None)
            sender = Runner.LocalPlayer;

        ApplySelect(sender, role);
    }

    // ------------------------------------------------------------------
    // Host 전용 상태 변경
    // ------------------------------------------------------------------

    private void ApplySelect(PlayerRef sender, Role role)
    {
        // 이미 누가 가져간 역할은 바꿀 수 없다.
        if (GetOwner(role) != PlayerRef.None)
            return;

        // 이 플레이어가 이미 역할을 확정했다면 더 이상 바꿀 수 없다.
        if (HasRole(sender))
            return;

        SetOwner(role, sender);
        RoleAssignments.Set(sender, role);

        TryScheduleStart();
    }

    private bool HasRole(PlayerRef player)
    {
        if (player == PlayerRef.None)
            return false;

        return MentalOwner == player || EarOwner == player;
    }

    private PlayerRef GetOwner(Role role)
    {
        return role == Role.mental ? MentalOwner : EarOwner;
    }

    private void SetOwner(Role role, PlayerRef player)
    {
        if (role == Role.mental)
            MentalOwner = player;
        else
            EarOwner = player;
    }

    /// <summary>다음 씬으로 넘어가도 되는 선택 상태인지.</summary>
    private bool IsSelectionComplete()
    {
        bool multi = NetworkManager.Instance == null
            || NetworkManager.Instance.Mode == SessionMode.Multi;

        if (multi)
            return MentalOwner != PlayerRef.None && EarOwner != PlayerRef.None;

        // Single(개인 테스트)은 하나만 골라도 진행한다.
        return MentalOwner != PlayerRef.None || EarOwner != PlayerRef.None;
    }

    private void TryScheduleStart()
    {
        if (!HasStateAuthority || _startRoutine != null)
            return;

        if (!IsSelectionComplete())
            return;

        _startRoutine = StartCoroutine(LoadNextSceneAfterDelay());
    }

    private void CancelScheduledStart()
    {
        if (_startRoutine == null)
            return;

        StopCoroutine(_startRoutine);
        _startRoutine = null;
    }

    private IEnumerator LoadNextSceneAfterDelay()
    {
        if (startDelaySeconds > 0f)
            yield return new WaitForSeconds(startDelaySeconds);

        // 기다리는 사이에 누가 선택을 취소했으면 출발하지 않는다.
        if (!IsSelectionComplete())
        {
            _startRoutine = null;
            yield break;
        }

        SceneRef nextScene = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetSceneRef(nextSceneName)
            : SceneRef.None;

        if (nextScene == SceneRef.None)
        {
            Debug.LogError($"[LobbyManager] 씬 '{nextSceneName}'을 찾을 수 없습니다. Build Profiles > Scene List를 확인하세요.", this);
            _startRoutine = null;
            yield break;
        }

        Debug.Log($"[LobbyManager] 역할 선택 완료 - mental={MentalOwner}, ear={EarOwner}. '{nextSceneName}' 로드.");
        Runner.LoadScene(nextScene);
    }

    /// <summary>세션에서 나간 플레이어가 잡고 있던 역할을 놓아 준다.</summary>
    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (!HasStateAuthority)
            return;

        if (MentalOwner != PlayerRef.None && !IsStillInSession(MentalOwner))
        {
            RoleAssignments.Remove(MentalOwner);
            MentalOwner = PlayerRef.None;
            CancelScheduledStart();
        }

        if (EarOwner != PlayerRef.None && !IsStillInSession(EarOwner))
        {
            RoleAssignments.Remove(EarOwner);
            EarOwner = PlayerRef.None;
            CancelScheduledStart();
        }
    }

    private bool IsStillInSession(PlayerRef player)
    {
        foreach (PlayerRef active in Runner.ActivePlayers)
        {
            if (active == player)
                return true;
        }

        return false;
    }

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------

    public override void Render()
    {
        base.Render();
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        if (Runner == null)
            return;

        PlayerRef me = Runner.LocalPlayer;
        bool iAlreadyChose = HasRole(me);

        ApplyButtonState(_mentalVisual, MentalOwner, iAlreadyChose);
        ApplyButtonState(_earVisual, EarOwner, iAlreadyChose);

        // 로컬 Role을 네트워크 상태에 맞춘다.
        if (MentalOwner == me)
            RoleManager.SetLocalRole(Role.mental);
        else if (EarOwner == me)
            RoleManager.SetLocalRole(Role.ear);
    }

    private void ApplyButtonState(RoleButtonVisual visual, PlayerRef owner, bool localAlreadyChose)
    {
        if (visual == null)
            return;

        bool taken = owner != PlayerRef.None;

        if (visual.Button != null)
        {
            // 누가 가져간 역할이거나, 내가 이미 역할을 확정했으면 잠근다.
            visual.Button.interactable = !taken && !localAlreadyChose;
        }

        // 회색 처리는 '선택됨' 기준이라 두 플레이어에게 똑같이 보인다.
        // (내가 이미 골라서 잠긴 반대쪽 버튼은 원래 색을 유지한다 — 아직 아무도 안 가져갔으니까)
        if (visual.Graphic != null)
            visual.Graphic.color = taken ? takenColor : visual.BaseColor;

        if (visual.Label != null)
            visual.Label.color = taken ? takenTextColor : visual.BaseLabelColor;
    }

    private void CacheButtonVisuals()
    {
        if (_mentalVisual == null)
            _mentalVisual = CreateVisual(mentalButton);

        if (_earVisual == null)
            _earVisual = CreateVisual(earButton);
    }

    private RoleButtonVisual CreateVisual(Button button)
    {
        if (button == null)
            return null;

        RoleButtonVisual visual = new RoleButtonVisual { Button = button };

        visual.Graphic = button.targetGraphic != null
            ? button.targetGraphic
            : button.GetComponent<Graphic>();

        if (visual.Graphic != null)
            visual.BaseColor = visual.Graphic.color;

        visual.Label = button.GetComponentInChildren<TMP_Text>(true);
        if (visual.Label != null)
            visual.BaseLabelColor = visual.Label.color;

        if (neutralizeDisabledTint)
        {
            // 버튼 transition이 ColorTint라 Disabled 색(기본 회색 + 알파 0.5)이 곱해진다.
            // 그대로 두면 우리가 지정한 회색이 반투명하게 흐려지므로 흰색(=곱해도 변화 없음)으로 바꾼다.
            ColorBlock colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
        }

        return visual;
    }

    /// <summary>버튼 하나의 원래 색을 기억해 두고 상태에 따라 갈아 끼우기 위한 묶음.</summary>
    private sealed class RoleButtonVisual
    {
        public Button Button;
        public Graphic Graphic;
        public TMP_Text Label;
        public Color BaseColor = Color.white;
        public Color BaseLabelColor = Color.white;
    }
}
