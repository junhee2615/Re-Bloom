using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

public class RightRayModeToggle : MonoBehaviour
{
    /// <summary>Persistent XR Rig에 하나만 존재한다. 다른 씬의 스크립트가 참조할 때 사용.</summary>
    public static RightRayModeToggle Instance { get; private set; }

    [Header("오른쪽 레이 오브젝트")]
    [SerializeField] private GameObject teleportInteractor;
    [SerializeField] private GameObject uiInteractor;

    [Header("시작 모드")]
    [SerializeField, Tooltip("켜면 아래 씬 목록과 무관하게 항상 startWithUIMode 값으로 시작한다.")]
    private bool overrideSceneStartMode = false;

    [SerializeField, Tooltip("overrideSceneStartMode가 켜져 있을 때만 사용하는 시작 모드.")]
    private bool startWithUIMode = false;

    [SerializeField, Tooltip("이 씬들에서는 UI 모드로 시작한다. 나머지 씬(스테이지)은 텔레포트 모드로 시작한다.")]
    private List<string> uiModeStartScenes = new List<string> { "StartScene", "Lobby" };

    [SerializeField, Tooltip("씬 전환 시 어떤 모드로 시작했는지 Console에 출력한다. (디버그용)")]
    private bool logSceneStartMode = true;

    private InputDevice rightController;

    // A 버튼 상태 → B 버튼 상태로 변경
    private bool previousBButtonState;

    // 현재 눌려 있는 B 입력이 다른 기능(개발용 Hold 등)에 소비되었는지.
    // 소비된 입력은 놓을 때 Ray Mode를 토글하지 않는다.
    private bool currentBPressConsumed;

    private bool isUIMode;

    /// <summary>현재 UI Ray 모드인지.</summary>
    public bool IsUIMode => isUIMode;

    private void Awake()
    {
        Instance = this;

        // XR Rig는 DontDestroyOnLoad라 Start가 한 번만 불린다.
        // 씬이 바뀔 때마다 그 씬에 맞는 시작 모드로 되돌리기 위해 콜백을 건다.
        // Fusion(NetworkSceneManagerDefault)은 씬을 Additive로 올린 뒤 SetActiveScene을 부르는 경로가 있어
        // sceneLoaded의 LoadSceneMode로 거르면 안 되고, activeSceneChanged도 함께 듣는다.
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        FindRightController();

        ApplySceneStartMode(SceneManager.GetActiveScene().name, "Start");
    }

    // Fusion은 Single/Additive 어느 쪽으로도 씬을 올릴 수 있으므로 모드로 거르지 않는다.
    // 이름을 아는 씬(uiModeStartScenes 또는 스테이지)만 처리해 부수 씬 로드에는 반응하지 않는다.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplySceneStartMode(scene.name, $"sceneLoaded({mode})");
    }

    // Fusion이 로드 후 SetActiveScene을 부를 때도 확실히 반영한다.
    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        ApplySceneStartMode(next.name, "activeSceneChanged");
    }

    // 씬 이름으로 시작 모드를 정한다. uiModeStartScenes에 있으면 UI 모드, 아니면 텔레포트 모드.
    private void ApplySceneStartMode(string sceneName, string source)
    {
        if (string.IsNullOrEmpty(sceneName))
            return;

        isUIMode = overrideSceneStartMode
            ? startWithUIMode
            : IsUIModeScene(sceneName);

        ApplyMode();

        if (logSceneStartMode)
        {
            Debug.Log(
                $"[RightRayModeToggle] {source} scene='{sceneName}' isUIMode={isUIMode} " +
                $"uiInteractor={(uiInteractor != null ? uiInteractor.activeSelf.ToString() : "null")} " +
                $"teleportInteractor={(teleportInteractor != null ? teleportInteractor.activeSelf.ToString() : "null")}",
                this);
        }
    }

    private bool IsUIModeScene(string sceneName)
    {
        if (uiModeStartScenes == null)
            return false;

        foreach (string name in uiModeStartScenes)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                string.Equals(name.Trim(), sceneName, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void Update()
    {
        if (!rightController.isValid)
        {
            FindRightController();
            return;
        }

        // primaryButton(A) → secondaryButton(B)
        if (!rightController.TryGetFeatureValue(
                CommonUsages.secondaryButton,
                out bool currentBButtonState))
        {
            return;
        }

        // B 버튼을 누르기 시작: 상태만 기록하고 아직 토글하지 않는다.
        if (currentBButtonState && !previousBButtonState)
        {
            currentBPressConsumed = false;
        }

        // B 버튼을 놓을 때: 다른 기능이 소비하지 않은 입력(짧게 누르기)만 토글한다.
        if (!currentBButtonState && previousBButtonState)
        {
            if (!currentBPressConsumed)
                ToggleMode();

            currentBPressConsumed = false;
        }

        previousBButtonState = currentBButtonState;
    }

    /// <summary>
    /// 지금 눌려 있는 B 입력을 다른 기능이 사용했다고 표시한다.
    /// 이후 버튼을 놓아도 Ray Mode는 토글되지 않는다. (다음 누름부터 다시 정상 동작)
    /// </summary>
    public void ConsumeCurrentBPress()
    {
        currentBPressConsumed = true;
    }

    /// <summary>Teleport 모드일 때만 UI Ray 모드로 바꾼다. 이미 UI 모드면 아무것도 하지 않는다.</summary>
    public void EnsureUIRayMode()
    {
        if (isUIMode)
            return;

        isUIMode = true;
        ApplyMode();
    }

    private void ToggleMode()
    {
        isUIMode = !isUIMode;
        ApplyMode();
    }

    private void FindRightController()
    {
        rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!rightController.isValid)
        {
            Debug.LogWarning(
                "[RightRayModeToggle] 오른쪽 XR 컨트롤러를 찾지 못했습니다.",
                this
            );
        }
    }

    private void ApplyMode()
    {
        if (isUIMode)
        {
            // UI 모드
            if (teleportInteractor != null)
                teleportInteractor.SetActive(false);

            if (uiInteractor != null)
                uiInteractor.SetActive(true);
        }
        else
        {
            // 텔레포트 대기 모드
            if (uiInteractor != null)
                uiInteractor.SetActive(false);

            // 바로 표시하지 않고 비활성 상태로 대기
            if (teleportInteractor != null)
                teleportInteractor.SetActive(false);
        }
    }
}
