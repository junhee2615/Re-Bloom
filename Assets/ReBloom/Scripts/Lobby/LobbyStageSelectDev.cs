using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// 개발용 Stage Select UI (Lobby/StageSelectCanvas).
///
/// - 오른손 B 버튼을 holdSeconds(5초) 동안 누르면 StageSelectCanvas를 열고 닫는다.
///   홀드가 성립한 입력은 RightRayModeToggle에 "소비됨"을 알려, 버튼을 놓아도 Ray Mode가 토글되지 않는다.
///   짧게 눌렀다 놓는 입력은 기존처럼 RightRayModeToggle이 Teleport ↔ UI Ray를 토글한다.
/// - 캔버스를 열 때는 UI Ray 모드를 보장해 바로 버튼을 누를 수 있게 한다. 닫을 때는 모드를 건드리지 않는다.
/// - Stage1/2/3 버튼 → LobbyManager.RequestNextSceneOverride(1/2/3).
///   값만 바꾸고 씬을 즉시 이동하지는 않는다. 실제 이동은 기존 캐릭터 선택 확정 흐름이 담당한다.
/// - Host/Client 동기화는 LobbyManager 쪽 RPC/[Networked] 값이 처리한다.
///
/// 이 컴포넌트는 항상 활성인 오브젝트에 두고, stageSelectCanvas GameObject만 켜고 끈다.
/// </summary>
public class LobbyStageSelectDev : MonoBehaviour
{
    [Header("Stage Select Canvas")]
    [SerializeField, Tooltip("B 버튼 홀드로 열고 닫을 캔버스. 기본 비활성으로 둔다.")]
    private GameObject stageSelectCanvas;

    [SerializeField, Min(0.1f), Tooltip("캔버스를 열고 닫기 위해 B 버튼을 계속 눌러야 하는 시간(초).")]
    private float holdSeconds = 5f;

    [Header("Stage Select 버튼")]
    [SerializeField, Tooltip("StageSelectCanvas/.../Stage1Btn")]
    private Button stage1Button;

    [SerializeField, Tooltip("StageSelectCanvas/.../Stage2Btn")]
    private Button stage2Button;

    [SerializeField, Tooltip("StageSelectCanvas/.../Stage3Btn")]
    private Button stage3Button;

    private bool subscribed;

    // B 버튼 홀드 감지
    private InputDevice rightController;
    private bool previousBButtonState;
    private float holdTimer;
    private bool holdConsumed;

    // 선택 표시. 버튼별 원본 ColorBlock을 보존했다가 선택이 바뀔 때 되돌린다.
    private static readonly string[] StageSceneNames = { "Stage1", "Stage2", "Stage3" };

    private Button[] stageButtons;
    private ColorBlock[] originalColors;
    private bool colorsCached;

    // 마지막으로 표시한 씬 이름. 바뀌었을 때만 ColorBlock을 다시 적용한다.
    private string displayedSceneName;

    // 로컬 클릭 직후 네트워크 값이 도착할 때까지 표시를 되돌리지 않기 위한 대기값.
    private string pendingSceneName;
    private float pendingSince;
    private const float PendingTimeout = 1f;

    private void Awake()
    {
        CacheButtonColors();
    }

    private void OnEnable()
    {
        Subscribe();
        ResetHold();

        if (IsCanvasOpen())
            RefreshSelection(force: true);
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetHold();

        // 버튼을 원래 Inspector 색으로 되돌린다.
        RestoreAllButtonColors();
        displayedSceneName = null;
        pendingSceneName = null;
    }

    private void Update()
    {
        UpdateHold();

        // 열려 있는 동안 네트워크 값(다른 피어의 변경 포함)을 따라간다.
        if (IsCanvasOpen())
            RefreshSelection(force: false);
    }

    // ------------------------------------------------------------------
    // B Button Hold
    // ------------------------------------------------------------------

    private void UpdateHold()
    {
        if (!rightController.isValid)
        {
            rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            if (!rightController.isValid)
                return;
        }

        if (!rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed))
            return;

        if (pressed)
        {
            // 누르기 시작
            if (!previousBButtonState)
                ResetHold();

            holdTimer += Time.unscaledDeltaTime;

            // 홀드 시간 도달: 한 번만 실행하고, 놓을 때까지 다시 실행하지 않는다.
            if (!holdConsumed && holdTimer >= holdSeconds)
            {
                holdConsumed = true;
                OnHoldReached();
            }
        }
        else if (previousBButtonState)
        {
            // 놓음: 다음 홀드를 다시 감지할 수 있게 초기화
            ResetHold();
        }

        previousBButtonState = pressed;
    }

    private void ResetHold()
    {
        holdTimer = 0f;
        holdConsumed = false;
    }

    private void OnHoldReached()
    {
        // 이 B 입력은 개발용 홀드로 사용했다. 놓아도 Ray Mode가 토글되지 않게 한다.
        RightRayModeToggle rayToggle = RightRayModeToggle.Instance;

        if (rayToggle != null)
            rayToggle.ConsumeCurrentBPress();

        if (stageSelectCanvas == null)
        {
            Debug.LogWarning("[LobbyStageSelectDev] Stage Select Canvas가 연결되지 않아 열고 닫을 수 없습니다.", this);
            return;
        }

        bool open = !stageSelectCanvas.activeSelf;
        stageSelectCanvas.SetActive(open);

        // 열리는 순간 LobbyManager의 실제 목적지를 읽어 표시를 맞춘다.
        if (open)
        {
            pendingSceneName = null;
            RefreshSelection(force: true);
        }

        // 열 때만 UI Ray를 보장한다. (이미 UI 모드면 EnsureUIRayMode가 아무것도 하지 않는다.)
        if (open && rayToggle != null)
            rayToggle.EnsureUIRayMode();

        Debug.Log($"[LobbyStageSelectDev] Stage Select Canvas {(open ? "opened" : "closed")}.", this);
    }

    // ------------------------------------------------------------------
    // Subscription
    // ------------------------------------------------------------------

    private void Subscribe()
    {
        if (subscribed)
            return;

        AddListener(stage1Button, OnStage1Clicked, "Stage1");
        AddListener(stage2Button, OnStage2Clicked, "Stage2");
        AddListener(stage3Button, OnStage3Clicked, "Stage3");

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (stage1Button != null)
            stage1Button.onClick.RemoveListener(OnStage1Clicked);

        if (stage2Button != null)
            stage2Button.onClick.RemoveListener(OnStage2Clicked);

        if (stage3Button != null)
            stage3Button.onClick.RemoveListener(OnStage3Clicked);

        subscribed = false;
    }

    private void AddListener(Button button, UnityEngine.Events.UnityAction action, string label)
    {
        if (button == null)
        {
            Debug.LogWarning($"[LobbyStageSelectDev] {label} Button이 연결되지 않았습니다.", this);
            return;
        }

        // 같은 메서드를 두 번 등록하지 않도록 먼저 제거한다.
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    // ------------------------------------------------------------------
    // Button Handlers
    // ------------------------------------------------------------------

    private void OnStage1Clicked()
    {
        SelectStage(1);
    }

    private void OnStage2Clicked()
    {
        SelectStage(2);
    }

    private void OnStage3Clicked()
    {
        SelectStage(3);
    }

    // NextSceneOverride 값만 바꾼다. 씬 이동은 기존 캐릭터 선택 확정 흐름이 담당한다.
    private void SelectStage(int stageIndex)
    {
        LobbyManager lobbyManager = LobbyManager.Instance;

        if (lobbyManager == null)
        {
            Debug.LogWarning(
                $"[LobbyStageSelectDev] LobbyManager가 아직 준비되지 않아 Stage {stageIndex} 선택을 적용할 수 없습니다.",
                this);
            return;
        }

        lobbyManager.RequestNextSceneOverride(stageIndex);

        // Client는 RPC 왕복 후에야 Networked 값이 바뀌므로, 먼저 로컬 표시를 갱신하고
        // 네트워크 값이 따라올 때까지(최대 PendingTimeout) 되돌리지 않는다.
        string sceneName = StageSceneNames[stageIndex - 1];
        pendingSceneName = sceneName;
        pendingSince = Time.unscaledTime;
        ApplySelection(sceneName);

        Debug.Log($"[LobbyStageSelectDev] Stage {stageIndex} selected.", this);
    }

    // ------------------------------------------------------------------
    // Selection Visual (Button ColorBlock)
    // ------------------------------------------------------------------

    private bool IsCanvasOpen()
    {
        return stageSelectCanvas != null && stageSelectCanvas.activeInHierarchy;
    }

    // 버튼별 원본 ColorBlock을 한 번만 저장한다.
    private void CacheButtonColors()
    {
        if (colorsCached)
            return;

        stageButtons = new[] { stage1Button, stage2Button, stage3Button };
        originalColors = new ColorBlock[stageButtons.Length];

        for (int i = 0; i < stageButtons.Length; i++)
        {
            if (stageButtons[i] != null)
                originalColors[i] = stageButtons[i].colors;
        }

        colorsCached = true;
    }

    // LobbyManager의 현재 목적지를 읽어 표시를 맞춘다. force=false면 바뀌었을 때만 적용한다.
    private void RefreshSelection(bool force)
    {
        LobbyManager lobbyManager = LobbyManager.Instance;

        if (lobbyManager == null)
        {
            if (force || displayedSceneName != null)
                ApplySelection(null);
            return;
        }

        string current = lobbyManager.CurrentNextSceneName;

        // 로컬 클릭 직후: 네트워크 값이 아직 따라오지 않았으면 잠시 기다린다.
        if (pendingSceneName != null)
        {
            if (current == pendingSceneName ||
                Time.unscaledTime - pendingSince > PendingTimeout)
            {
                pendingSceneName = null;
            }
            else if (!force)
            {
                return;
            }
        }

        if (!force && current == displayedSceneName)
            return;

        ApplySelection(current);
    }

    // 정확히 하나의 버튼만 선택 상태로 보이게 한다. (없으면 전부 원본 색)
    private void ApplySelection(string sceneName)
    {
        CacheButtonColors();

        for (int i = 0; i < stageButtons.Length; i++)
        {
            Button button = stageButtons[i];

            if (button == null)
                continue;

            ColorBlock colors = originalColors[i];

            if (sceneName != null && sceneName == StageSceneNames[i])
            {
                // Inspector의 Selected Color를 평소/Hover 색으로 써서 EventSystem 상태와 무관하게 유지한다.
                colors.normalColor = originalColors[i].selectedColor;
                colors.highlightedColor = originalColors[i].selectedColor;
            }

            button.colors = colors;
        }

        displayedSceneName = sceneName;
    }

    private void RestoreAllButtonColors()
    {
        if (!colorsCached)
            return;

        for (int i = 0; i < stageButtons.Length; i++)
        {
            if (stageButtons[i] != null)
                stageButtons[i].colors = originalColors[i];
        }
    }
}
