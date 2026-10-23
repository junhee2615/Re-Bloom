using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 미션 현장 근처에 떠 있는 <b>조작 설명</b> 보드. MISSION 1(수로 복구)용으로 만들었다.
///
/// 왼손 <c>TutorialCanvas</c> 의 <c>UIPanel</c> 과 역할이 다르다 —
///  · <c>UIPanel</c> : "무엇을 해야 하는지"(미션 목표). 영속 리그에 하나뿐이고 X 버튼으로 여닫는다.
///  · 이 보드   : "어떻게 조작하는지"(STEP 1~3 + 담당 캐릭터 + 완료 안내).
///               현장에 놓여 있고 가까이 가면 저절로 펼쳐진다.
/// 그래서 문구는 목표를 되풀이하지 않고 버튼·판정 기준만 적는다.
///
/// 담당 캐릭터 칸(<see cref="CharacterDisplay"/>)은 <b>표시 전용</b>이다. 누가 입력할 수 있는지는
/// 미션 코드가 정하고, 이 보드는 그 판정을 읽지도 바꾸지도 않는다.
///
/// 표시 조건은 셋이고 모두 만족해야 한다 —
///  ① <see cref="TutorialMissionManager_2.CurrentTutorial"/> 가 <see cref="visibleStep"/> 과 같다
///  ② 컷씬·씬 로딩 중이 아니다 (<see cref="hideDuringCutscene"/>)
///  ③ 로컬 카메라가 <see cref="showDistance"/> 안에 있다
/// 단계가 넘어가면 ①이 깨져 저절로 페이드아웃된다. 숨기는 쪽 처리를 따로 부를 필요가 없다.
///
/// ②가 필요한 이유: 수로 미션이 끝나면 <c>WaterMissionManager</c> 가
/// <c>Mission1ClearCutscene.TryPlay(NotifyMissionCleared)</c> 를 호출하는데, 그 콜백은
/// 컷씬이 <b>끝날 때</b> 실행된다(<c>Mission1ClearCutscene.PlayRoutine</c> 말미의
/// <c>InvokeCompletion()</c>). 즉 컷씬이 도는 내내 단계는 아직 Initial 이고, 그 사이
/// 컷씬 카메라가 보드 옆을 지나가면 설명이 그대로 보인다. 컷씬마다 참조를 연결하지 않고
/// 끄려면 이미 있는 공통 신호가 필요한데, <c>ItemEffectClock.IsGameplayActive</c> 가
/// 바로 그것이다 — 씬 로딩과 Stage1~3 컷씬 재생 상태를 한곳에서 모아 본다.
///
/// 이 컴포넌트가 <b>건드리지 않는 것</b> — 미션 판정, Fusion 상태, 다른 UI.
/// <c>CurrentTutorial</c> 을 읽기만 하고 아무것도 쓰지 않는다. 표시 여부는 전부 로컬 판단이라
/// 두 플레이어가 서로 다른 곳에 서 있으면 각자 다른 결론이 난다(그게 맞다).
///
/// 배치: 수로 입구(<c>WaterwayZone</c>) 근처의 World Space Canvas. Canvas에는
/// <c>GraphicRaycaster</c> 를 붙이지 않고 Collider도 두지 않는다. 하위 Graphic의
/// <c>raycastTarget</c> 은 <see cref="Awake"/> 에서 한 번 더 꺼서 Poke / Near-Far /
/// Teleport Interactor의 판정을 가로채지 않게 한다.
///
/// 거리 기준점은 <see cref="anchor"/> 로 잡는다. 보드가 눈높이에 떠 있으면
/// 비워 둔 채로는 "현장에서 3m"가 보드 높이만큼 어긋난다.
/// </summary>
[AddComponentMenu("ReBloom/Stage2/Mission Guide Board")]
public class MissionGuideBoard : MonoBehaviour
{
    /// <summary>
    /// 카메라와 기준점 사이의 거리를 재는 방식.
    /// </summary>
    /// <remarks>
    /// 수로 미션처럼 <b>기준점이 플레이어보다 낮은 곳</b>에 있으면 3D 거리가 문제가 된다.
    /// 판을 읽으려고 둑으로 올라서는 순간 높이차가 거리에 그대로 더해져 판이 꺼져 버린다.
    /// <see cref="Horizontal"/> 은 Y를 무시하므로 발밑 높이가 달라져도 표시가 유지된다.
    /// </remarks>
    public enum DistanceMode
    {
        /// <summary>높이차까지 포함한 3D 거리.</summary>
        Distance3D = 0,

        /// <summary>XZ 평면 거리. 높이차를 무시한다.</summary>
        Horizontal = 1,
    }

    /// <summary>
    /// 이 보드가 안내하는 담당 캐릭터. <b>표시만</b> 바꾸며 실제 역할 제한과는 무관하다.
    /// </summary>
    /// <remarks>
    /// MISSION 1(수로 복구)에는 역할 제한이 아예 없다 — <c>Assets/ReBloom/Scripts/Stage2/Water/</c>
    /// 어느 파일에도 <c>RoleManager</c> / <c>MissionRole</c> 참조가 없다. 그래서 기본값은
    /// <see cref="Both"/> 다(바위가 서로 다른 두 사람을 요구하므로 둘 다 보여 주는 쪽이 맞다).
    /// 한쪽 역할만 조작하는 미션에서 이 보드를 다시 쓸 때를 위해 나머지 두 모드를 둔다.
    /// </remarks>
    public enum CharacterDisplay
    {
        /// <summary>루멘·에코 모두 표시.</summary>
        Both = 0,

        /// <summary>루멘만 표시.</summary>
        LumenOnly = 1,

        /// <summary>에코만 표시.</summary>
        EchoOnly = 2,
    }

    /// <summary>
    /// STEP 한 칸. 연결(어느 TMP/Image에 쓸지)과 내용(무엇을 쓸지)을 함께 들고 있다.
    /// 연결만 해 두고 내용을 비우면 그 칸은 TMP에 입력된 문구를 그대로 쓴다.
    /// </summary>
    [Serializable]
    public class GuideStep
    {
        [Header("연결")]
        [Tooltip("'01 · 나뭇가지 뽑기' 처럼 칸 번호와 제목을 쓰는 TMP. 비워도 된다.")]
        public TMP_Text labelText;

        [Tooltip("설명 본문 TMP.")]
        public TMP_Text bodyText;

        [Tooltip("칸 아이콘 Image. 비워도 된다.")]
        public Image iconImage;

        [Header("내용")]
        [Tooltip("칸 번호와 제목. 비우면 labelText를 건드리지 않는다.")]
        public string label = string.Empty;

        [Tooltip("조작 설명. 비우면 bodyText를 건드리지 않는다.")]
        [TextArea(3, 8)]
        public string body = string.Empty;

        [Tooltip("칸 아이콘. 비우면 iconImage를 건드리지 않는다.")]
        public Sprite icon;
    }

    [Header("참조")]
    [Tooltip("페이드에 쓰는 CanvasGroup. 비우면 이 오브젝트에서 찾는다.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("제목 위 머리말 TMP. 'MISSION 01 · 조작 가이드' 같은 줄.")]
    [SerializeField] private TMP_Text kickerText;

    [Tooltip("보드 제목 TMP.")]
    [SerializeField] private TMP_Text titleText;

    [Tooltip("거리를 재는 기준 Transform. 보통 WaterwayZone을 연결한다. " +
             "비우면 이 보드 자신의 위치를 쓴다(보드가 눈높이에 있으면 그만큼 오차가 난다).")]
    [SerializeField] private Transform anchor;

    [Header("내용")]
    [Tooltip("제목 위 머리말. 비우면 kickerText를 건드리지 않는다.")]
    [SerializeField] private string kicker = "MISSION 01 · 조작 가이드";

    [Tooltip("보드 제목. 비우면 titleText를 건드리지 않는다.")]
    [SerializeField] private string title = "수로 복구 방법";

    [Tooltip("STEP 칸들. MISSION 1은 3칸이다.")]
    [SerializeField]
    private List<GuideStep> steps = new List<GuideStep>
    {
        new GuideStep
        {
            label = "01 · 나뭇가지 뽑기",
            body =
                "나뭇가지를 Grab 버튼으로 잡고 당겨서 뽑으세요.\n" +
                "뽑은 나뭇가지는 빛나는 경계선 밖으로 옮기세요.",
        },
        new GuideStep
        {
            label = "02 · 바위 옮기기",
            body =
                "루멘과 에코가 함께 바위를 잡으세요.\n" +
                "두 사람이 잡으면 움직일 수 있어요.\n" +
                "경계선 밖으로 옮기세요.",
        },
        new GuideStep
        {
            label = "03 · 흙덩이 제거하기",
            body =
                "흙을 Grab으로 잡으면 한 줌이 떠지고 덩이가 작아져요.\n" +
                "손에 든 흙을 놓고, 한 덩이당 3번 반복하세요.",
        },
    };

    [Header("완료 안내")]
    [Tooltip("보드 하단 완료 안내 TMP.")]
    [SerializeField] private TMP_Text footerText;

    [Tooltip("완료 안내 문구. 비우면 footerText를 건드리지 않는다.")]
    [TextArea(2, 5)]
    [SerializeField] private string footer = "모두 제거하면 수로가 복구됩니다.";

    [Header("담당 캐릭터")]
    [Tooltip("누구를 안내할지. 표시만 바뀌며 실제 역할 제한 로직과는 무관하다. " +
             "수로 미션은 역할 제한이 없고 바위가 두 사람을 요구하므로 Both가 기본이다.")]
    [SerializeField] private CharacterDisplay characterDisplay = CharacterDisplay.Both;

    [Tooltip("루멘 초상 Image. Sprite는 Inspector에서 직접 연결한다.")]
    [SerializeField] private Image lumenPortrait;

    [Tooltip("에코 초상 Image. Sprite는 Inspector에서 직접 연결한다.")]
    [SerializeField] private Image echoPortrait;

    [Tooltip("루멘 이름 TMP.")]
    [SerializeField] private TMP_Text lumenNameText;

    [Tooltip("에코 이름 TMP.")]
    [SerializeField] private TMP_Text echoNameText;

    [Tooltip("루멘 표시 이름.")]
    [SerializeField] private string lumenName = "루멘";

    [Tooltip("에코 표시 이름.")]
    [SerializeField] private string echoName = "에코";

    [Tooltip("캐릭터 칸 옆 참여 안내 TMP.")]
    [SerializeField] private TMP_Text participationText;

    [Tooltip("Both 일 때의 참여 안내 문구.")]
    [SerializeField] private string participationBoth = "두 캐릭터 모두 참여할 수 있어요.";

    [Tooltip("LumenOnly 일 때의 참여 안내 문구.")]
    [SerializeField] private string participationLumenOnly = "루멘이 담당하는 구간이에요.";

    [Tooltip("EchoOnly 일 때의 참여 안내 문구.")]
    [SerializeField] private string participationEchoOnly = "에코가 담당하는 구간이에요.";

    [Header("표시 조건")]
    [Tooltip("이 단계일 때만 보인다. MISSION 1 조작 설명은 Initial.")]
    [SerializeField] private TutorialStep_2 visibleStep = TutorialStep_2.Initial;

    [Tooltip("컷씬·씬 로딩 중에는 숨긴다. 미션 완료 컷씬이 도는 동안에도 단계는 아직 " +
             "넘어가지 않으므로, 이것을 끄면 컷씬 카메라가 지나갈 때 설명이 보인다.")]
    [SerializeField] private bool hideDuringCutscene = true;

    [Header("거리")]
    [Tooltip("거리를 재는 방식. 기준점이 플레이어보다 낮은 곳(수로 바닥 등)에 있으면 " +
             "Horizontal 을 쓴다. 둑으로 올라서도 판이 꺼지지 않는다.")]
    [SerializeField] private DistanceMode distanceMode = DistanceMode.Horizontal;

    [Tooltip("이 거리(m) 안으로 들어오면 나타난다.")]
    [SerializeField, Min(0f)] private float showDistance = 6f;

    [Tooltip("사라지는 거리를 이만큼(m) 더 멀게 잡는다. 경계에 서 있을 때 깜빡이는 것을 막는다.")]
    [SerializeField, Min(0f)] private float hideHysteresis = 0.5f;

    [Header("페이드")]
    [Tooltip("나타나고 사라지는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.3f;

    [Tooltip("완전히 숨겨졌을 때 Canvas 렌더링까지 끈다. 보이지 않는 UI를 그리지 않게 한다.")]
    [SerializeField] private bool disableCanvasWhenHidden = true;

    [Tooltip("보드와 같은 자리에 깊이만 찍어 주는 Renderer. 화면 안개가 보드를 " +
             "'배경 깊이'로 덮어 흐려지는 것을 막는다. 보드가 숨겨지면 함께 꺼진다.")]
    [SerializeField] private Renderer fogDepthPrime;

    [Header("Billboard")]
    [Tooltip("로컬 카메라를 향해 회전한다.")]
    [SerializeField] private bool billboard = true;

    [Tooltip("수평을 유지한다. 끄면 올려다보거나 내려다볼 때 보드가 꺾여 읽기 어렵다.")]
    [SerializeField] private bool lockPitch = true;

    [Header("테마")]
    [Tooltip("강조색을 적용할 Graphic들. 테두리 조각과 구분선을 넣는다.")]
    [SerializeField] private Graphic[] accentGraphics;

    [Tooltip("강조색. 수로 미션은 역할 제한이 없어 역할색 대신 물빛을 쓴다.")]
    [SerializeField] private Color accentColor = new Color(0.30980393f, 0.76862746f, 0.84705883f, 1f);

    [Tooltip("제목에도 강조색을 쓸지. 끄면 제목은 TMP에 지정된 색을 유지한다.")]
    [SerializeField] private bool tintTitle;

    [Header("디버그")]
    [Tooltip("표시/숨김이 바뀔 때 로그를 남긴다.")]
    [SerializeField] private bool logVisibility;

    // 매니저를 못 찾았을 때 다시 찾기까지의 간격(초).
    // 스폰 전에는 찾아도 없으므로 매 프레임 Find 하지 않도록 둔다.
    private const float ManagerSearchInterval = 0.5f;

    // 지금 보여야 하는지. 히스테리시스 때문에 이전 상태를 들고 있어야 한다.
    private bool visible;

    private Canvas ownCanvas;
    private Camera cachedCamera;
    private float alpha;

    // 마지막으로 확인한 단계. 이벤트로도 들어오고 매니저에서 직접 읽기도 한다.
    private TutorialStep_2 currentStep = TutorialStep_2.None;

    private TutorialMissionManager_2 manager;
    private float nextManagerSearch;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        ownCanvas = GetComponent<Canvas>();

        if (canvasGroup != null)
        {
            // 읽기 전용 보드다. 런타임에도 한 번 더 못 박는다.
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        DisableRaycastTargets();
        ApplyContent();
        ApplyAccent();

        // 멀리서 시작한다. 조건이 맞으면 첫 프레임부터 페이드인된다.
        visible = false;
        alpha = 0f;
        ApplyAlpha(0f);
    }

    private void OnEnable()
    {
        TutorialMissionManager_2.TutorialChanged += OnTutorialChanged;

        // 비활성 상태로 시작했다가 뒤늦게 켜진 경우, 이미 지나간 이벤트는 받을 수 없다.
        // 그래서 켜질 때 매니저에서 현재 단계를 직접 읽어 맞춰 둔다.
        nextManagerSearch = 0f;
        RefreshStepFromManager();
    }

    private void OnDisable()
    {
        TutorialMissionManager_2.TutorialChanged -= OnTutorialChanged;
    }

    private void OnTutorialChanged(TutorialStep_2 step)
    {
        currentStep = step;
    }

    /// <summary>
    /// 카메라 포즈가 갱신된 뒤에 보려고 LateUpdate를 쓴다.
    /// TrackedPoseDriver가 Update 단계에서 HMD 위치를 옮기므로, 여기서 읽는 값이 최신이다.
    /// (<c>ItemDescriptionBoard</c> / <c>ItemPickupPromptUI</c> 도 같은 이유로 LateUpdate를 쓴다)
    /// </summary>
    private void LateUpdate()
    {
        RefreshStepFromManager();

        Camera cam = ResolveCamera();

        if (cam == null)
        {
            // 카메라를 못 찾으면 안전하게 숨긴다. 뚝 끊지 않고 페이드아웃시킨다.
            SetVisible(false);
            UpdateFade();
            return;
        }

        SetVisible(ShouldShow(cam));
        UpdateFade();

        if (billboard)
            UpdateBillboard(cam);
    }

    /// <summary>
    /// 로컬 XR 카메라. null이 될 때만 다시 찾는다.
    /// </summary>
    /// <remarks>
    /// Opening에서는 <c>OpeningEnvironmentController</c> 가 MainCamera 태그를 잠시 떼므로
    /// <see cref="Camera.main"/> 이 null이 될 수 있다. 그 구간에도 예외가 나지 않아야 한다.
    /// </remarks>
    private Camera ResolveCamera()
    {
        if (cachedCamera != null)
            return cachedCamera;

        cachedCamera = Camera.main;
        return cachedCamera;
    }

    /// <summary>
    /// 매니저에서 현재 단계를 읽어 온다.
    /// </summary>
    /// <remarks>
    /// <see cref="TutorialMissionManager_2.TutorialChanged"/> 만 믿으면 두 군데서 어긋난다 —
    /// 이벤트는 <c>None</c> 을 발행하지 않고, 보드가 뒤늦게 켜지면 이미 지나간 발행을 놓친다.
    /// 그래서 복제된 값을 직접 읽는 쪽을 기준으로 삼고 이벤트는 빠른 경로로만 쓴다.
    ///
    /// <c>[Networked]</c> 프로퍼티는 스폰 전·디스폰 후에 접근할 수 없으므로
    /// <c>Object.IsValid</c> 로 걸러야 한다(<c>TutorialMissionManager_2</c> 내부의
    /// <c>IsReady</c> 와 같은 판정이다).
    /// 매니저를 못 찾는 동안에는 <see cref="ManagerSearchInterval"/> 간격으로만 다시 찾는다.
    /// </remarks>
    private void RefreshStepFromManager()
    {
        if (manager == null)
        {
            if (Time.unscaledTime < nextManagerSearch)
                return;

            nextManagerSearch = Time.unscaledTime + ManagerSearchInterval;
            manager = FindAnyObjectByType<TutorialMissionManager_2>();

            if (manager == null)
                return;
        }

        if (manager.Object == null || !manager.Object.IsValid)
            return;

        currentStep = manager.CurrentTutorial;
    }

    private bool ShouldShow(Camera cam)
    {
        // 지정 단계가 아니면 거리를 보지 않는다. 단계가 넘어가면 여기서 저절로 꺼진다.
        if (currentStep != visibleStep)
            return false;

        // 컷씬·로딩 중에는 숨긴다. 미션 완료 컷씬 동안 단계가 아직 Initial인 구간을 덮는다.
        if (hideDuringCutscene && !ItemEffectClock.IsGameplayActive)
            return false;

        Vector3 origin = anchor != null ? anchor.position : transform.position;
        Vector3 delta = cam.transform.position - origin;

        // Horizontal 이면 높이차를 버린다. 둑 위로 올라서도 거리가 늘지 않는다.
        if (distanceMode == DistanceMode.Horizontal)
            delta.y = 0f;

        // 보이는 중이면 조금 더 멀어져야 꺼진다(히스테리시스).
        float threshold = visible ? showDistance + hideHysteresis : showDistance;

        // sqrt를 피한다. 임계값도 함께 제곱해 비교한다.
        return delta.sqrMagnitude <= threshold * threshold;
    }

    private void SetVisible(bool value)
    {
        if (visible == value)
            return;

        visible = value;

        if (logVisibility)
            Debug.Log($"[MissionGuideBoard] {name}: {(value ? "표시" : "숨김")} (단계 {currentStep})", this);
    }

    private void UpdateFade()
    {
        float target = visible ? 1f : 0f;

        if (!Mathf.Approximately(alpha, target))
        {
            float step = fadeDuration <= 0f
                ? 1f
                : Time.deltaTime / fadeDuration;

            alpha = Mathf.MoveTowards(alpha, target, step);
        }

        ApplyAlpha(alpha);
    }

    private void ApplyAlpha(float value)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = value;

        bool shown = value > 0.001f;

        if (disableCanvasWhenHidden && ownCanvas != null)
        {
            if (ownCanvas.enabled != shown)
                ownCanvas.enabled = shown;
        }

        // 깊이 프라임은 보드가 보일 때만 켠다.
        // 숨겨진 동안에도 깊이를 찍으면, 아무것도 없는 허공에 안개가 걷힌 사각형이 남는다.
        if (fogDepthPrime != null && fogDepthPrime.enabled != shown)
            fogDepthPrime.enabled = shown;
    }

    /// <summary>
    /// 카메라를 향하도록 회전한다.
    /// </summary>
    /// <remarks>
    /// 카메라 <b>반대</b> 방향을 LookRotation에 넣는다. UI의 앞면은 로컬 -Z를 보고 있어,
    /// +Z를 카메라에서 멀어지는 쪽으로 돌려야 앞면이 카메라를 향한다.
    /// (<c>ItemDescriptionBoard</c> 가 쓰는 것과 같은 식이다)
    /// </remarks>
    private void UpdateBillboard(Camera cam)
    {
        Vector3 fromCamera = transform.position - cam.transform.position;

        if (lockPitch)
            fromCamera.y = 0f;

        if (fromCamera.sqrMagnitude <= 0.000001f)
            return;

        transform.rotation = Quaternion.LookRotation(fromCamera, Vector3.up);
    }

    // ── 내용 / 테마 / 안전 설정 ──────────────────────────────────────────────

    /// <summary>
    /// 제목·STEP 문구·완료 안내·담당 캐릭터를 연결된 TMP·Image에 반영한다.
    /// 내용이 빈 칸은 건드리지 않으므로, TMP에 직접 입력한 문구를 쓰고 싶으면 비워 두면 된다.
    /// </summary>
    public void ApplyContent()
    {
        if (kickerText != null && !string.IsNullOrEmpty(kicker))
            kickerText.text = kicker;

        if (titleText != null && !string.IsNullOrEmpty(title))
            titleText.text = title;

        if (footerText != null && !string.IsNullOrEmpty(footer))
            footerText.text = footer;

        if (steps != null)
        {
            foreach (GuideStep step in steps)
            {
                if (step == null)
                    continue;

                if (step.labelText != null && !string.IsNullOrEmpty(step.label))
                    step.labelText.text = step.label;

                if (step.bodyText != null && !string.IsNullOrEmpty(step.body))
                    step.bodyText.text = step.body;

                if (step.iconImage != null && step.icon != null)
                    step.iconImage.sprite = step.icon;
            }
        }

        ApplyCharacters();
    }

    /// <summary>
    /// <see cref="characterDisplay"/> 에 맞춰 담당 캐릭터 칸을 켜고 끄고, 이름을 써 넣는다.
    /// </summary>
    /// <remarks>
    /// 끌 때 <c>Graphic.enabled</c> 가 아니라 GameObject를 끈다. 레이아웃 그룹 안에 있을 때
    /// 한쪽만 표시하면 남은 칸이 가운데로 모여야 자연스럽기 때문이다. 초상과 이름을 각각
    /// 끄므로 둘이 같은 부모를 쓰는지 여부와 무관하게 동작한다.
    ///
    /// 초상 Sprite는 건드리지 않는다 — Inspector에서 직접 연결할 값이다.
    /// 역할 판정(<c>RoleManager</c>)도 보지 않는다. 이 보드는 "누가 하는 미션인지" 설명만 하고,
    /// 실제로 누가 입력할 수 있는지는 미션 코드가 정한다.
    /// </remarks>
    public void ApplyCharacters()
    {
        bool showLumen = characterDisplay != CharacterDisplay.EchoOnly;
        bool showEcho = characterDisplay != CharacterDisplay.LumenOnly;

        if (lumenNameText != null && !string.IsNullOrEmpty(lumenName))
            lumenNameText.text = lumenName;

        if (echoNameText != null && !string.IsNullOrEmpty(echoName))
            echoNameText.text = echoName;

        if (participationText != null)
        {
            string note = ResolveParticipationNote();

            if (!string.IsNullOrEmpty(note))
                participationText.text = note;
        }

        SetGraphicActive(lumenPortrait, showLumen);
        SetGraphicActive(lumenNameText, showLumen);
        SetGraphicActive(echoPortrait, showEcho);
        SetGraphicActive(echoNameText, showEcho);
    }

    /// <summary>표시 모드에 맞는 참여 안내 문구. 비어 있으면 TMP를 건드리지 않는다.</summary>
    private string ResolveParticipationNote()
    {
        switch (characterDisplay)
        {
            case CharacterDisplay.LumenOnly:
                return participationLumenOnly;

            case CharacterDisplay.EchoOnly:
                return participationEchoOnly;

            default:
                return participationBoth;
        }
    }

    private static void SetGraphicActive(Component target, bool active)
    {
        if (target == null)
            return;

        GameObject go = target.gameObject;

        if (go.activeSelf != active)
            go.SetActive(active);
    }

    /// <summary>
    /// <see cref="accentColor"/> 를 <see cref="accentGraphics"/> 에 반영한다.
    /// 각 Graphic의 알파는 그대로 두어, 테두리와 구분선의 농도 차이를 유지한다.
    /// </summary>
    public void ApplyAccent()
    {
        if (accentGraphics != null)
        {
            foreach (Graphic graphic in accentGraphics)
            {
                if (graphic == null)
                    continue;

                graphic.color = new Color(
                    accentColor.r, accentColor.g, accentColor.b, graphic.color.a);
            }
        }

        if (tintTitle && titleText != null)
        {
            titleText.color = new Color(
                accentColor.r, accentColor.g, accentColor.b, titleText.color.a);
        }
    }

    /// <summary>
    /// 하위 모든 Graphic의 raycastTarget을 끈다.
    /// 프리팹에서 이미 꺼 두더라도, 나중에 자식을 추가했을 때 XR 입력을 막지 않게 한다.
    /// </summary>
    private void DisableRaycastTargets()
    {
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.raycastTarget)
                graphic.raycastTarget = false;
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 문구·담당 캐릭터·강조색을 바꾸면 바로 보이게 한다.
    /// </summary>
    /// <remarks>
    /// 한 프레임 미뤄서 실행하는 이유: <see cref="ApplyCharacters"/> 가 담당 캐릭터 칸을
    /// <c>SetActive</c> 로 켜고 끄는데, Unity는 OnValidate 안에서의 SetActive를 거부하고
    /// "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate" 를 띄운다.
    /// <c>delayCall</c> 로 빠져나온 뒤에 적용하면 같은 결과를 경고 없이 얻는다.
    /// 그 사이 컴포넌트가 지워졌을 수 있어 람다 안에서 null을 다시 확인한다.
    /// </remarks>
    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        UnityEditor.EditorApplication.delayCall += ApplyInEditor;
    }

    private void ApplyInEditor()
    {
        UnityEditor.EditorApplication.delayCall -= ApplyInEditor;

        // 미뤄 둔 사이에 컴포넌트나 오브젝트가 사라졌을 수 있다.
        if (this == null || Application.isPlaying)
            return;

        ApplyContent();
        ApplyAccent();
    }
#endif
}
