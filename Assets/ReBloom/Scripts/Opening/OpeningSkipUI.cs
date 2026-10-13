using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opening 스킵 안내 문구와 원형 홀드 게이지.
///
/// 입력 판정은 <see cref="OpeningSkipInput"/>, 종료 순서는 OpeningNarrationController가
/// 갖고 있고 이 스크립트는 **표시만** 한다. 두 스크립트의 값은 읽기만 하며
/// (<see cref="OpeningSkipInput.IsHolding"/> / <see cref="OpeningSkipInput.HoldProgress01"/> /
/// <c>OpeningNarrationController.IsFinishing</c>) 어떤 값도 되돌려 쓰지 않는다.
///
/// 왜 런타임 생성인가:
/// VR에서 쓸 수 있는 캔버스는 영속 XR Origin 안의 ScreenFadeCanvas 하나뿐이다
/// (World Space / Sorting Order 32767). Opening 씬에 별도 캔버스를 두면 그 검정을
/// 이길 수 없다. 그런데 ScreenFadeCanvas는 XR Origin 프리팹 소유이므로 프리팹을
/// 수정하지 않으려면 런타임에 자식으로 넣는 수밖에 없다.
/// 내레이션 텍스트(OpeningText)가 쓰는 방식과 같다.
///
/// 계층과 CanvasGroup:
/// ScreenFade가 알파를 제어하는 것은 **자식 Image**의 CanvasGroup이다(프리팹의
/// canvasGroup 참조). 그래서 그 Image의 형제로 들어온 이 UI는 검정 알파의 영향을
/// 받지 않는다. 드로우 순서는 하이어라키 순서이므로 Image 바로 뒤에 넣어
/// 검정보다 앞, OpeningText보다 뒤가 되게 한다.
///
/// 게이지 구조 (멀티 공동 스킵 확장 대비):
/// 진행 링을 하나의 Radial360 Image로 그리지 않고 **반원 세그먼트 2개**로 나눈다.
/// 세그먼트 0은 12시→6시(Origin Top, 시계방향), 세그먼트 1은 6시→12시
/// (Origin Bottom, 시계방향)를 담당하며 각자 fillAmount 0~0.5를 쓴다.
/// 혼자 홀드할 때는 한 진행도를 두 세그먼트에 이어서 나눠 넣고(0~50% / 50~100%),
/// 나중에 두 플레이어가 각각 반원을 채우는 공동 스킵에서는
/// <see cref="SetSegmentProgress"/> 로 세그먼트를 독립적으로 밀면 된다.
/// 그라데이션은 텍스처의 **각도 공간**에 구워 두므로 어느 세그먼트가 어디까지
/// 드러내든 12시에서 시계방향으로 색이 이어진다.
///
/// 영속 캔버스 아래에 들어가므로 Opening이 끝나면 반드시 지워야 한다.
/// 그대로 두면 Lobby에서 안내 문구가 계속 떠 있다.
/// </summary>
public class OpeningSkipUI : MonoBehaviour
{
    /// <summary>진행 링을 나누는 세그먼트 수. 공동 스킵에서 플레이어 1명당 1개를 쓴다.</summary>
    public const int SegmentCount = 2;

    /// <summary>세그먼트 하나가 담당하는 fillAmount 범위. (원 전체가 1)</summary>
    private const float SegmentFill = 1f / SegmentCount;

    [Header("안내 문구")]
    [SerializeField, TextArea(1, 3)]
    private string hintText = "Y 버튼을 3초간 길게 눌러 스킵";

    [SerializeField, Min(0f)] private float hintFadeInDuration = 0.6f;

    [Tooltip("완전히 보인 상태로 유지하는 시간. 이 뒤에 페이드아웃된다.")]
    [SerializeField, Min(0f)] private float hintHoldDuration = 5f;

    [SerializeField, Min(0f)] private float hintFadeOutDuration = 0.8f;
    [SerializeField, Min(1f)] private float hintFontSize = 28f;
    [SerializeField] private Color hintColor = new Color(0.86f, 0.88f, 0.86f, 1f);

    [Header("원형 게이지 - 크기")]
    [Tooltip("게이지 지름(캔버스 단위).")]
    [SerializeField, Min(10f)] private float gaugeSize = 120f;

    [Tooltip("링 두께(캔버스 단위). 120 지름에 6이면 가늘고 단정한 테두리가 된다.")]
    [SerializeField, Min(1f)] private float gaugeThickness = 6f;

    [Header("원형 게이지 - 색")]
    [Tooltip("채워지지 않은 기본 링. #2A2A2A / Alpha 0.4")]
    [SerializeField]
    private Color gaugeTrackColor = new Color(0.16470589f, 0.16470589f, 0.16470589f, 0.4f);

    [Tooltip("진행 링 그라데이션 시작색(12시). #A7F3BF")]
    [SerializeField]
    private Color gaugeFillStartColor = new Color(0.654902f, 0.9529412f, 0.7490196f, 1f);

    [Tooltip("진행 링 그라데이션 끝색(한 바퀴 돌아온 지점). #6EE7B7")]
    [SerializeField]
    private Color gaugeFillEndColor = new Color(0.43137255f, 0.90588236f, 0.7176471f, 1f);

    [Header("원형 게이지 - Glow")]
    [Tooltip("진행 링 뒤에 깔리는 번짐의 두께(캔버스 단위). 링 두께보다 커야 번져 보인다.")]
    [SerializeField, Min(1f)] private float glowThickness = 18f;

    [Tooltip("평상시 Glow 세기.")]
    [SerializeField, Range(0f, 1f)] private float glowBaseAlpha = 0.22f;

    [Tooltip("완료 직전 Glow 세기. 밝기가 살짝 올라가는 연출이다.")]
    [SerializeField, Range(0f, 1f)] private float glowPeakAlpha = 0.6f;

    [Tooltip("이 진행도부터 Glow가 glowPeakAlpha를 향해 올라간다.")]
    [SerializeField, Range(0f, 1f)] private float glowBoostStartProgress = 0.82f;

    [Header("원형 게이지 - 완료 발광")]
    [Tooltip("완료 순간 원 전체가 짧게 발광하는 시간.")]
    [SerializeField, Min(0f)] private float completionFlashDuration = 0.28f;

    [SerializeField, Range(0f, 1f)] private float completionFlashAlpha = 0.85f;

    [Tooltip("이 진행도 이상에서 종료가 시작되면 '완료'로 보고 발광을 재생한다. " +
             "중간에 놓은 경우(취소)와 구분하기 위한 값이다.")]
    [SerializeField, Range(0.5f, 1f)] private float completionThreshold = 0.9f;

    [Header("원형 게이지 - 등장 / 퇴장")]
    [Tooltip("Y를 누르기 시작할 때 게이지 전체가 올라오는 시간.")]
    [SerializeField, Min(0f)] private float gaugeFadeInDuration = 0.2f;

    [Tooltip("Y를 놓거나 오프닝이 끝날 때 게이지 전체가 사라지는 시간. " +
             "완전히 투명해진 뒤에 비활성화된다.")]
    [SerializeField, Min(0f)] private float gaugeFadeOutDuration = 0.25f;

    [Header("중앙 Y 버튼")]
    [SerializeField] private string gaugeLabel = "Y";

    [Tooltip("중앙 버튼 지름(캔버스 단위).")]
    [SerializeField, Min(4f)] private float centerButtonSize = 36f;

    [Tooltip("어두운 반투명 원형 배경.")]
    [SerializeField] private Color centerButtonColor = new Color(0.07f, 0.08f, 0.08f, 0.55f);

    [Tooltip("얇은 회색 테두리.")]
    [SerializeField] private Color centerButtonBorderColor = new Color(0.62f, 0.65f, 0.64f, 0.55f);

    [Tooltip("테두리 두께(캔버스 단위).")]
    [SerializeField, Min(0.5f)] private float centerButtonBorderThickness = 1.6f;

    [SerializeField, Min(1f)] private float gaugeLabelFontSize = 20f;
    [SerializeField] private Color gaugeLabelColor = Color.white;

    [Header("ScreenFadeCanvas 안에서의 배치")]
    [Tooltip("ScreenFadeCanvas 기준 로컬 Z. 내레이션 텍스트와 같은 값을 쓴다.")]
    [SerializeField] private float uiDistance = 1.6f;

    [Tooltip("ScreenFadeCanvas는 localScale 1.4의 거대한 캔버스다. 그 안에서 줄이는 배율.")]
    [SerializeField] private float uiLocalScale = 0.00167f;

    [Tooltip("게이지 위치(캔버스 단위). 내레이션 패널은 y 0 기준 ±75라 겹치지 않는다.")]
    [SerializeField] private Vector2 gaugeAnchoredPosition = new Vector2(0f, -230f);

    [SerializeField] private Vector2 hintAnchoredPosition = new Vector2(0f, -400f);
    [SerializeField] private Vector2 hintPanelSize = new Vector2(1000f, 120f);

    [Header("References")]
    [Tooltip("비워 두면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private OpeningSkipInput skipInput;

    [Tooltip("비워 두면 같은 오브젝트에서 찾는다. UI 정리를 위해 IsFinishing만 읽는다.")]
    [SerializeField] private OpeningNarrationController narrationController;

    [Tooltip("한글 글리프가 있는 TMP 폰트. 비워 두면 Opening 씬의 내레이션 폰트를 쓴다.")]
    [SerializeField] private TMP_FontAsset fontAsset;

    [Tooltip("진행 링 스프라이트를 직접 지정할 때만 쓴다. 비워 두면 런타임에 만든다. " +
             "직접 지정하면 각도 그라데이션이 구워져 있지 않아 단색으로 보인다.")]
    [SerializeField] private Sprite ringSpriteOverride;

    [Tooltip("런타임 링 스프라이트의 한 변 픽셀 수. 한 번만 만든다.")]
    [SerializeField, Min(64)] private int ringResolution = 256;

    [Tooltip("중앙 버튼용 작은 스프라이트의 한 변 픽셀 수. 한 번만 만든다.")]
    [SerializeField, Min(32)] private int discResolution = 128;

    /// <summary>진행 링 한 조각. 세그먼트당 코어 링과 Glow 링을 함께 움직인다.</summary>
    private sealed class GaugeSegment
    {
        public Image Core;
        public Image Glow;
        public float Progress = -1f;   // -1 = 아직 한 번도 쓰지 않음
    }

    /// <summary>
    /// true면 <see cref="Update"/> 가 진행도를 쓰지 않는다.
    /// 멀티 공동 스킵처럼 바깥에서 세그먼트를 직접 밀 때 켠다.
    /// (이번 단계에서는 네트워크 기능을 구현하지 않으므로 기본 false다.)
    /// </summary>
    public bool ExternalProgressDrive { get; set; }

    // 런타임에 만든 것들. 전부 이 스크립트가 책임지고 지운다.
    private RectTransform uiRoot;
    private GameObject gaugeRoot;

    // 게이지 루트 전체의 알파. 링 / Glow / 중앙 Y가 한 번에 함께 페이드된다.
    // 세그먼트 Glow의 color.a와는 곱해지기만 하므로 서로 충돌하지 않는다.
    private CanvasGroup gaugeGroup;

    private GaugeSegment[] segments;
    private Image completionFlash;
    private CanvasGroup hintGroup;

    private Sprite ringCoreSprite;
    private Sprite ringGlowSprite;
    private Sprite discSprite;
    private Sprite thinRingSprite;
    private Texture2D ringCoreTexture;
    private Texture2D ringGlowTexture;
    private Texture2D discTexture;
    private Texture2D thinRingTexture;

    private float appliedGlowAlpha = -1f;
    private float appliedFlashAlpha = -1f;
    private float appliedGaugeAlpha = -1f;
    private float lastTotalProgress;

    // 게이지 등장/퇴장. 코루틴 없이 Update에서 목표 알파로 이동시킨다.
    private float gaugeAlpha;

    // ExternalProgressDrive가 켜져 있을 때 SetGaugeVisible이 넣는 표시 희망값.
    private bool externalGaugeVisible;

    private bool cleanedUp;
    private bool finishHandled;

    private void Awake()
    {
        if (skipInput == null)
            skipInput = GetComponent<OpeningSkipInput>();

        if (narrationController == null)
            narrationController = GetComponent<OpeningNarrationController>();

        // 폰트는 반드시 Awake에서 해석한다.
        // 내레이션 텍스트는 OpeningNarrationController.Start()에서 영속 캔버스로
        // 옮겨지며 Opening 씬을 떠나므로, Start 시점에는 씬 탐색으로 찾을 수 없다.
        fontAsset = ResolveFontAsset();

        if (skipInput == null)
        {
            Debug.LogWarning(
                "[Opening] OpeningSkipInput을 찾지 못해 스킵 UI를 표시하지 않습니다.", this);

            enabled = false;
        }
    }

    private IEnumerator Start()
    {
        if (!BuildUI())
            yield break;

        yield return ShowHint();
    }

    private void Update()
    {
        if (cleanedUp)
            return;

        // 정상 종료든 스킵이든 종료가 시작되면 치운다.
        // ScreenFadeCanvas는 영속이므로 남겨 두면 Lobby까지 따라간다.
        if (narrationController != null && narrationController.IsFinishing)
        {
            if (finishHandled)
                return;

            finishHandled = true;
            HandleFinish();
            return;
        }

        if (gaugeRoot == null || skipInput == null)
            return;

        bool holding = skipInput.IsHolding;

        // 공동 스킵 쪽에서 직접 제어할 때는 SetGaugeVisible이 넣은 값을 따른다.
        bool shouldShow = ExternalProgressDrive ? externalGaugeVisible : holding;

        UpdateGaugeFade(shouldShow);

        if (ExternalProgressDrive)
            return;

        // 놓으면 HoldProgress01이 0이 되므로 별도 초기화 로직이 필요하지 않다.
        // 페이드 중에도 이 값을 그대로 반영하므로 진행도는 정상적으로 증가한다.
        lastTotalProgress = holding ? skipInput.HoldProgress01 : 0f;

        ApplySoloProgress(lastTotalProgress);
        ApplyGlow(lastTotalProgress);
    }

    // ── 게이지 등장 / 퇴장 ──────────────────────────────────────────────────

    /// <summary>
    /// 게이지 전체를 목표 알파로 한 프레임만큼 움직인다.
    ///
    /// 코루틴을 쓰지 않는 이유:
    /// 누르고 놓기를 빠르게 반복하면 코루틴을 매번 띄우고 멈추게 되는데, 그러면
    /// 할당이 늘고 "Fade Out 도중 재입력"에서 두 코루틴이 같은 알파를 다투게 된다.
    /// <see cref="Mathf.MoveTowards"/> 로 현재 알파에서 목표로 수렴시키면 상태가
    /// 하나뿐이라 알파 점프도, 깜빡임도 생기지 않는다.
    ///
    /// 켜는 순서가 중요하다 — 알파 0으로 먼저 활성화하고 그 뒤에 올린다.
    /// 반대로 하면 첫 프레임에 알파 1로 한 번 보인다.
    /// 끄는 것은 완전히 투명해진 뒤에만 한다.
    /// </summary>
    private void UpdateGaugeFade(bool show)
    {
        if (gaugeRoot == null)
            return;

        if (show && !gaugeRoot.activeSelf)
        {
            SetGaugeAlpha(gaugeAlpha);   // 지금 알파(보통 0)를 먼저 반영
            gaugeRoot.SetActive(true);
        }

        float target = show ? 1f : 0f;

        if (!Mathf.Approximately(gaugeAlpha, target))
        {
            float duration = target > gaugeAlpha ? gaugeFadeInDuration : gaugeFadeOutDuration;

            SetGaugeAlpha(duration > 0f
                ? Mathf.MoveTowards(gaugeAlpha, target, Time.unscaledDeltaTime / duration)
                : target);
        }

        if (!show && gaugeAlpha <= 0f && gaugeRoot.activeSelf)
            gaugeRoot.SetActive(false);
    }

    /// <summary>
    /// 게이지 루트 CanvasGroup의 알파만 건드린다. ScreenFade의 CanvasGroup과는
    /// 다른 오브젝트이므로 서로 독립적으로 동작한다.
    /// 값이 거의 같으면 쓰지 않아 불필요한 캔버스 갱신을 막는다. (0과 1은 항상 쓴다)
    /// </summary>
    private void SetGaugeAlpha(float alpha)
    {
        gaugeAlpha = Mathf.Clamp01(alpha);

        if (gaugeGroup == null)
            return;

        bool endpoint = gaugeAlpha <= 0f || gaugeAlpha >= 1f;

        if (!endpoint && Mathf.Abs(gaugeAlpha - appliedGaugeAlpha) < 0.002f)
            return;

        appliedGaugeAlpha = gaugeAlpha;
        gaugeGroup.alpha = gaugeAlpha;
    }

    /// <summary>페이드를 건너뛰고 즉시 완전히 보이게 한다. 완료 발광 직전에 쓴다.</summary>
    private void ShowGaugeImmediate()
    {
        if (gaugeRoot == null)
            return;

        SetGaugeAlpha(1f);

        if (!gaugeRoot.activeSelf)
            gaugeRoot.SetActive(true);
    }

    private void OnDestroy()
    {
        CleanupUI("destroy");
    }

    // ── 진행도 반영 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 한 사람이 홀드할 때의 진행도. 0~50%는 세그먼트 0, 50~100%는 세그먼트 1에
    /// 이어서 넣어 원 하나가 12시부터 시계방향으로 채워지는 것처럼 보이게 한다.
    /// </summary>
    private void ApplySoloProgress(float progress01)
    {
        float p = Mathf.Clamp01(progress01) * SegmentCount;

        for (int i = 0; i < SegmentCount; i++)
            SetSegmentProgress(i, Mathf.Clamp01(p - i));
    }

    /// <summary>
    /// 세그먼트 하나의 진행도를 직접 넣는다. 0 = 비었음, 1 = 그 반원이 꽉 찬 상태.
    ///
    /// 멀티 공동 스킵의 확장 지점이다 — 플레이어 A를 세그먼트 0,
    /// 플레이어 B를 세그먼트 1에 연결하면 둘이 각각 반원을 채운다.
    /// 그때는 <see cref="ExternalProgressDrive"/> 를 켜서 Update가 덮어쓰지 않게 한다.
    ///
    /// 값이 거의 같으면 쓰지 않는다. fillAmount 대입은 그래픽을 dirty로 만들어
    /// 캔버스 메시를 다시 만들게 하므로, 변화가 없을 때의 리빌드를 막는다.
    /// </summary>
    public void SetSegmentProgress(int index, float progress01)
    {
        if (segments == null || index < 0 || index >= segments.Length)
            return;

        GaugeSegment segment = segments[index];

        if (segment == null)
            return;

        float p = Mathf.Clamp01(progress01);

        if (Mathf.Abs(p - segment.Progress) < 0.0005f)
            return;

        segment.Progress = p;

        float fill = p * SegmentFill;

        if (segment.Core != null)
            segment.Core.fillAmount = fill;

        if (segment.Glow != null)
            segment.Glow.fillAmount = fill;
    }

    /// <summary>
    /// 게이지 표시 여부. 공동 스킵 쪽에서 직접 제어할 때 쓴다.
    /// 즉시 켜거나 끄지 않고 <see cref="UpdateGaugeFade"/> 가 페이드로 처리한다.
    /// <see cref="ExternalProgressDrive"/> 가 켜져 있을 때만 반영된다.
    /// </summary>
    public void SetGaugeVisible(bool visible)
    {
        externalGaugeVisible = visible;
    }

    /// <summary>
    /// 완료 직전 Glow를 올린다. 색은 텍스처에 구워져 있으므로 알파만 움직인다.
    /// (Image.color는 1을 넘을 수 없어 코어 링 자체를 더 밝게 만들 수는 없다.
    ///  밝아 보이는 효과는 뒤에 깔린 Glow의 세기로 낸다.)
    /// </summary>
    private void ApplyGlow(float progress01)
    {
        float boost = Mathf.InverseLerp(glowBoostStartProgress, 1f, progress01);
        float alpha = Mathf.Lerp(glowBaseAlpha, glowPeakAlpha, boost);

        if (Mathf.Abs(alpha - appliedGlowAlpha) < 0.002f)
            return;

        appliedGlowAlpha = alpha;

        if (segments == null)
            return;

        foreach (GaugeSegment segment in segments)
        {
            if (segment?.Glow == null)
                continue;

            Color color = segment.Glow.color;
            color.a = alpha;
            segment.Glow.color = color;
        }
    }

    private void SetFlashAlpha(float alpha)
    {
        if (completionFlash == null)
            return;

        if (Mathf.Abs(alpha - appliedFlashAlpha) < 0.002f)
            return;

        appliedFlashAlpha = alpha;

        Color color = completionFlash.color;
        color.a = alpha;
        completionFlash.color = color;
    }

    // ── 종료 처리 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 종료가 시작됐다. 홀드를 끝까지 채워서 들어온 경우에만 짧게 발광시키고 치운다.
    ///
    /// 진행도를 직전 프레임 값으로 판정하는 이유:
    /// OpeningSkipInput은 3초에 도달한 프레임에 heldTime을 0으로 돌리고 스킵을
    /// 요청한다. 같은 GameObject에서 Update 순서가 Input → UI 이므로 이 메서드가
    /// 불리는 시점의 HoldProgress01은 이미 0이다. 그래서 직전 프레임에 기록해 둔
    /// <see cref="lastTotalProgress"/> 로 "완료"와 "중간에 놓음"을 구분한다.
    ///
    /// 발광은 스킵의 검정 페이드(기본 0.6초)보다 짧으므로 Lobby 전환을 늦추지 않는다.
    /// </summary>
    private void HandleFinish()
    {
        bool completed = lastTotalProgress >= completionThreshold;
        bool visible = gaugeAlpha > 0f;

        // 보이지도 않고 완료도 아니면(정상 오프닝 종료의 일반적인 경우) 바로 치운다.
        if (!completed && !visible)
        {
            CleanupUI("finishing");
            return;
        }

        if (completed)
        {
            // 마지막 프레임의 진행도가 1에 아주 조금 못 미칠 수 있다. 12시 틈을 메운다.
            for (int i = 0; i < SegmentCount; i++)
                SetSegmentProgress(i, 1f);

            ApplyGlow(1f);
            ShowGaugeImmediate();
        }

        StartCoroutine(FinishPresentationThenCleanup(completed));
    }

    /// <summary>
    /// 완료 발광 → 게이지 Fade Out → 정리.
    ///
    /// 스킵 흐름을 붙잡지 않는다 — OpeningNarrationController는 이미 자기 코루틴에서
    /// 검정 페이드(기본 0.6초)와 Stage 언로드를 진행하고 있고, 이 연출은 그와
    /// 나란히 돈다. 씬이 먼저 언로드되면 <c>OnDestroy</c> 의 CleanupUI가 즉시 정리한다.
    /// </summary>
    private IEnumerator FinishPresentationThenCleanup(bool completed)
    {
        if (completed && completionFlashDuration > 0f && completionFlash != null)
            yield return PlayCompletionFlash();

        yield return FadeGaugeOutForExit();

        CleanupUI(completed ? "completed" : "finishing");
    }

    private IEnumerator PlayCompletionFlash()
    {
        float elapsed = 0f;

        while (elapsed < completionFlashDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            if (completionFlash == null)
                break;

            float t = Mathf.Clamp01(elapsed / completionFlashDuration);

            // 빠르게 올라갔다가 천천히 사라진다.
            float alpha = t < 0.25f
                ? Mathf.Lerp(0f, completionFlashAlpha, t / 0.25f)
                : Mathf.Lerp(completionFlashAlpha, 0f, (t - 0.25f) / 0.75f);

            SetFlashAlpha(alpha);
            yield return null;
        }

        SetFlashAlpha(0f);
    }

    /// <summary>
    /// 종료 경로 전용 Fade Out. Update는 finishHandled로 이미 손을 뗀 상태이므로
    /// 여기서 직접 알파를 내린다. 같은 <see cref="SetGaugeAlpha"/> 를 쓴다.
    /// </summary>
    private IEnumerator FadeGaugeOutForExit()
    {
        if (gaugeGroup == null || gaugeAlpha <= 0f)
            yield break;

        if (gaugeFadeOutDuration <= 0f)
        {
            SetGaugeAlpha(0f);
            yield break;
        }

        while (gaugeAlpha > 0f)
        {
            if (gaugeGroup == null)
                yield break;

            SetGaugeAlpha(
                Mathf.MoveTowards(gaugeAlpha, 0f, Time.unscaledDeltaTime / gaugeFadeOutDuration));

            yield return null;
        }
    }

    // ── UI 생성 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ScreenFadeCanvas 아래에 안내 문구와 게이지를 만든다.
    /// 스프라이트도 여기서 한 번만 만든다. 매 프레임 텍스처를 만들지 않는다.
    /// 성공하면 true. 캔버스를 찾지 못하면 아무것도 만들지 않고 false.
    /// </summary>
    private bool BuildUI()
    {
        ScreenFade screenFade = FindAnyObjectByType<ScreenFade>(FindObjectsInactive.Include);

        if (screenFade == null)
        {
            Debug.LogWarning(
                "[Opening] ScreenFade를 찾지 못해 스킵 UI를 만들지 않았습니다. " +
                "영속 XR Origin의 ScreenFadeCanvas가 살아 있는지 확인하세요.", this);

            return false;
        }

        Canvas fadeCanvas = screenFade.GetComponent<Canvas>();

        if (fadeCanvas == null)
        {
            Debug.LogWarning(
                "[Opening] ScreenFade가 붙은 오브젝트에서 Canvas를 찾지 못해 " +
                "스킵 UI를 만들지 않았습니다.", this);

            return false;
        }

        int layer = fadeCanvas.gameObject.layer;

        // ── 스프라이트 4종을 시작 시 한 번만 만든다 ─────────────
        CreateSprites();

        // ── 루트 ────────────────────────────────────────────────
        uiRoot = CreateRect("OpeningSkipUI", fadeCanvas.transform, layer);

        uiRoot.localScale = new Vector3(uiLocalScale, uiLocalScale, uiLocalScale);
        uiRoot.sizeDelta = Vector2.zero;

        // anchoredPosition이 localPosition의 x·y를 덮으므로 z는 마지막에 넣는다.
        uiRoot.localPosition = new Vector3(0f, 0f, uiDistance);

        PlaceAboveBlackImage(uiRoot, screenFade);

        // ── 게이지 ──────────────────────────────────────────────
        RectTransform gauge = CreateRect("SkipGauge", uiRoot, layer);
        gauge.anchoredPosition = gaugeAnchoredPosition;
        gauge.sizeDelta = new Vector2(gaugeSize, gaugeSize);

        gaugeRoot = gauge.gameObject;

        // 게이지 전체를 한 번에 페이드하는 CanvasGroup.
        // 링 / Glow / 중앙 Y가 모두 이 자식이라 함께 올라오고 함께 사라진다.
        // 세그먼트 Glow의 color.a는 이 알파와 곱해지기만 하므로 충돌하지 않는다.
        gaugeGroup = gaugeRoot.AddComponent<CanvasGroup>();
        gaugeGroup.alpha = 0f;
        gaugeGroup.interactable = false;
        gaugeGroup.blocksRaycasts = false;

        gaugeAlpha = 0f;
        appliedGaugeAlpha = 0f;

        // 기본 링 (항상 꽉 찬 상태)
        CreateStretchedImage("GaugeTrack", gauge, layer, ringCoreSprite, gaugeTrackColor);

        // 진행 세그먼트. Glow를 먼저 깔고 그 위에 코어 링을 올린다.
        segments = new GaugeSegment[SegmentCount];

        for (int i = 0; i < SegmentCount; i++)
        {
            // 세그먼트 0 = 12시부터 시계방향, 세그먼트 1 = 6시부터 시계방향.
            // 둘을 합치면 원 한 바퀴가 끊김 없이 이어진다.
            int origin = i == 0
                ? (int)Image.Origin360.Top
                : (int)Image.Origin360.Bottom;

            Color glowColor = Color.white;
            glowColor.a = glowBaseAlpha;

            Image glow = CreateStretchedImage(
                $"Segment{i}_Glow", gauge, layer, ringGlowSprite, glowColor);

            Image core = CreateStretchedImage(
                $"Segment{i}_Core", gauge, layer, ringCoreSprite, Color.white);

            MakeRadialFilled(glow, origin);
            MakeRadialFilled(core, origin);

            segments[i] = new GaugeSegment { Core = core, Glow = glow };
        }

        // 완료 순간의 짧은 발광. 원 전체를 한 번에 밝히므로 Filled가 아니다.
        Color flashColor = Color.white;
        flashColor.a = 0f;

        completionFlash = CreateStretchedImage(
            "CompletionFlash", gauge, layer, ringGlowSprite, flashColor);

        appliedFlashAlpha = 0f;

        // ── 중앙 Y 버튼 ─────────────────────────────────────────
        RectTransform center = CreateRect("CenterButton", gauge, layer);
        center.sizeDelta = new Vector2(centerButtonSize, centerButtonSize);

        CreateStretchedImage("CenterBackground", center, layer, discSprite, centerButtonColor);
        CreateStretchedImage("CenterBorder", center, layer, thinRingSprite, centerButtonBorderColor);

        TextMeshProUGUI label = CreateText(
            "CenterLabel", center, layer, gaugeLabel, gaugeLabelFontSize, gaugeLabelColor);

        label.rectTransform.sizeDelta = new Vector2(centerButtonSize, centerButtonSize);

        // 눌렀을 때만 보인다.
        gaugeRoot.SetActive(false);

        // ── 안내 문구 ───────────────────────────────────────────
        TextMeshProUGUI hint = CreateText(
            "SkipHint", uiRoot, layer, hintText, hintFontSize, hintColor);

        hint.rectTransform.anchoredPosition = hintAnchoredPosition;
        hint.rectTransform.sizeDelta = hintPanelSize;

        hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0f;
        hintGroup.interactable = false;
        hintGroup.blocksRaycasts = false;

        Debug.Log("[Opening] Skip UI attached to ScreenFadeCanvas", this);

        return true;
    }

    /// <summary>
    /// 검정 Image 바로 뒤 형제로 넣는다.
    ///
    /// 같은 캔버스에서는 하이어라키 순서가 드로우 순서다. 검정 뒤에 두면 검정 위에
    /// 그려지고, OpeningText는 <c>SetAsLastSibling</c>으로 항상 맨 뒤에 들어가므로
    /// 이 UI보다 위에 그려진다. 내레이션 쪽 Start가 먼저 돌든 나중에 돌든
    /// 최종 순서는 [Image, SkipUI, OpeningText]로 같다.
    /// </summary>
    private void PlaceAboveBlackImage(RectTransform root, ScreenFade screenFade)
    {
        Transform blackImage = ResolveBlackImage(screenFade);

        if (blackImage == null)
        {
            root.SetAsLastSibling();
            return;
        }

        root.SetSiblingIndex(blackImage.GetSiblingIndex() + 1);
    }

    /// <summary>
    /// ScreenFade가 알파를 제어하는 검정 Image를 찾는다.
    ///
    /// ScreenFade.canvasGroup은 private 직렬화 필드라 밖에서 읽을 수 없고
    /// ScreenFade.cs는 수정 대상이 아니다. 프리팹에서 그 참조는 자식 Image의
    /// CanvasGroup이고 루트에도 별개의 CanvasGroup이 하나 더 있다. 그래서
    /// 루트를 제외하고 Image 컴포넌트까지 가진 것만 고른다
    /// (OpeningText의 CanvasGroup은 TMP라 Image가 없어 걸러진다).
    /// </summary>
    private Transform ResolveBlackImage(ScreenFade screenFade)
    {
        foreach (CanvasGroup group in screenFade.GetComponentsInChildren<CanvasGroup>(true))
        {
            if (group.gameObject == screenFade.gameObject)
                continue;

            if (group.GetComponent<Image>() == null)
                continue;

            return group.transform;
        }

        return null;
    }

    private IEnumerator ShowHint()
    {
        if (hintGroup == null)
            yield break;

        yield return FadeHint(0f, 1f, hintFadeInDuration);

        if (hintHoldDuration > 0f)
            yield return new WaitForSeconds(hintHoldDuration);

        yield return FadeHint(1f, 0f, hintFadeOutDuration);

        // 문구만 치운다. 게이지는 남아 있어 이후에도 스킵 진행도를 보여준다.
        if (hintGroup != null)
        {
            Destroy(hintGroup.gameObject);
            hintGroup = null;

            Debug.Log("[Opening] Skip hint hidden", this);
        }
    }

    private IEnumerator FadeHint(float from, float to, float duration)
    {
        if (hintGroup == null)
            yield break;

        if (duration <= 0f)
        {
            hintGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            if (hintGroup == null)
                yield break;

            hintGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        if (hintGroup != null)
            hintGroup.alpha = to;
    }

    // ── 정리 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 만든 것을 전부 지운다. 영속 캔버스 아래에 있으므로 반드시 불려야 한다.
    /// 몇 번 불려도 안전하다.
    /// </summary>
    private void CleanupUI(string reason)
    {
        if (cleanedUp)
            return;

        cleanedUp = true;

        gaugeRoot = null;
        gaugeGroup = null;
        segments = null;
        completionFlash = null;
        hintGroup = null;

        if (uiRoot != null)
        {
            Destroy(uiRoot.gameObject);
            uiRoot = null;
        }

        // 런타임에 만든 스프라이트/텍스처는 씬 언로드로 회수되지 않는다.
        DestroyGenerated(ref ringCoreSprite, ref ringCoreTexture);
        DestroyGenerated(ref ringGlowSprite, ref ringGlowTexture);
        DestroyGenerated(ref discSprite, ref discTexture);
        DestroyGenerated(ref thinRingSprite, ref thinRingTexture);

        Debug.Log($"[Opening] Skip UI cleaned up ({reason})");
    }

    private void DestroyGenerated(ref Sprite sprite, ref Texture2D texture)
    {
        // 인스펙터로 넘겨받은 스프라이트는 우리 것이 아니므로 지우지 않는다.
        // 런타임 생성물만 texture가 함께 채워져 있다.
        if (texture == null)
        {
            sprite = null;
            return;
        }

        if (sprite != null)
        {
            Destroy(sprite);
            sprite = null;
        }

        Destroy(texture);
        texture = null;
    }

    // ── 생성 헬퍼 ───────────────────────────────────────────────────────────

    private static RectTransform CreateRect(string name, Transform parent, int layer)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = layer;

        var rect = (RectTransform)go.transform;

        if (parent != null)
            rect.SetParent(parent, false);

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition = Vector2.zero;

        return rect;
    }

    /// <summary>부모 크기를 그대로 채우는 Image 하나. UI Ray는 받지 않는다.</summary>
    private Image CreateStretchedImage(
        string name, Transform parent, int layer, Sprite sprite, Color color)
    {
        RectTransform rect = CreateRect(name, parent, layer);

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = Image.Type.Simple;

        // UI Ray 입력을 차단하지 않는다.
        image.raycastTarget = false;

        return image;
    }

    /// <summary>Radial 360 / 시계방향 / 지정한 원점으로 바꾼다. 처음에는 비어 있다.</summary>
    private static void MakeRadialFilled(Image image, int origin360)
    {
        if (image == null)
            return;

        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Radial360;
        image.fillOrigin = origin360;
        image.fillClockwise = true;
        image.fillAmount = 0f;
    }

    private TextMeshProUGUI CreateText(
        string name, Transform parent, int layer, string text, float fontSize, Color color)
    {
        RectTransform rect = CreateRect(name, parent, layer);

        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();

        if (fontAsset != null)
            tmp.font = fontAsset;

        tmp.text = text;
        tmp.enableAutoSizing = false;
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = color;
        tmp.raycastTarget = false;

        return tmp;
    }

    // ── 스프라이트 생성 (시작 시 1회) ───────────────────────────────────────

    /// <summary>
    /// 필요한 스프라이트 4종을 한 번만 만든다.
    ///
    /// Shader / RenderTexture를 쓰지 않는다 — 전부 평범한 RGBA32 텍스처라 기본 UI
    /// 머티리얼로 그려지고 Quest Single Pass Instanced에서도 그대로 동작한다.
    /// Filled 이미지는 Tight 메시에서 어긋나므로 모두 FullRect로 만든다.
    /// </summary>
    private void CreateSprites()
    {
        int ring = Mathf.Max(64, ringResolution);
        int disc = Mathf.Max(32, discResolution);

        // 두께는 "지름 대비 비율"로 바꿔 텍스처 픽셀에 대응시킨다.
        float coreRatio = Mathf.Clamp(gaugeThickness / Mathf.Max(1f, gaugeSize), 0.004f, 0.45f);
        float glowRatio = Mathf.Clamp(glowThickness / Mathf.Max(1f, gaugeSize), coreRatio, 0.48f);
        float borderRatio =
            Mathf.Clamp(centerButtonBorderThickness / Mathf.Max(1f, centerButtonSize), 0.01f, 0.45f);

        if (ringSpriteOverride != null)
        {
            ringCoreSprite = ringSpriteOverride;   // texture가 null이라 정리에서 건드리지 않는다
        }
        else
        {
            ringCoreTexture = BuildRingTexture(ring, coreRatio, gradient: true, soft: false);
            ringCoreSprite = BuildSprite(ringCoreTexture, "SkipRingCore");
        }

        ringGlowTexture = BuildRingTexture(ring, glowRatio, gradient: true, soft: true);
        ringGlowSprite = BuildSprite(ringGlowTexture, "SkipRingGlow");

        discTexture = BuildDiscTexture(disc);
        discSprite = BuildSprite(discTexture, "SkipCenterDisc");

        thinRingTexture = BuildRingTexture(disc, borderRatio, gradient: false, soft: false);
        thinRingSprite = BuildSprite(thinRingTexture, "SkipCenterBorder");
    }

    /// <summary>
    /// 링(도넛) 텍스처.
    ///
    /// gradient가 켜져 있으면 색을 **각도 공간**에 구워 넣는다. 12시에서 시계방향으로
    /// <see cref="gaugeFillStartColor"/> → <see cref="gaugeFillEndColor"/> 로 변하며,
    /// Radial360 / Origin Top / 시계방향 채우기가 드러내는 순서와 정확히 일치한다.
    /// 그래서 Image.color를 흰색으로 두면 채워지는 부분이 곧 그라데이션이 되고,
    /// 세그먼트를 둘로 나눠도 색이 이어진다. (셰이더가 필요 없는 이유)
    ///
    /// soft가 켜져 있으면 링 중심선에서 바깥으로 부드럽게 사라지는 번짐이 된다.
    /// 꺼져 있으면 경계만 1.5px 깎아 안티앨리어싱한다.
    /// </summary>
    private Texture2D BuildRingTexture(int size, float thicknessRatio, bool gradient, bool soft)
    {
        var texture = NewTexture(size, "SkipRing");

        float center = (size - 1) * 0.5f;
        float outer = center;
        float thickness = Mathf.Max(1f, thicknessRatio * size);
        float inner = Mathf.Max(0.5f, outer - thickness);
        float mid = (outer + inner) * 0.5f;
        float halfWidth = Mathf.Max(1f, (outer - inner) * 0.5f);
        const float feather = 1.5f;
        const float twoPi = Mathf.PI * 2f;

        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float dy = y - center;

            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha;

                if (soft)
                {
                    // 중심선에서 멀어질수록 제곱으로 사라진다.
                    float n = Mathf.Clamp01(1f - Mathf.Abs(distance - mid) / halfWidth);
                    alpha = n * n;
                }
                else
                {
                    // 바깥 경계와 안쪽 경계를 각각 1.5px 깎아 계단을 없앤다.
                    alpha = Mathf.Min(
                        Mathf.Clamp01((outer - distance) / feather),
                        Mathf.Clamp01((distance - inner) / feather));
                }

                Color color = Color.white;

                if (gradient && alpha > 0f)
                {
                    // 12시(+y)에서 시계방향(+x)으로 0 → 1.
                    float angle = Mathf.Atan2(dx, dy);
                    if (angle < 0f)
                        angle += twoPi;

                    color = Color.Lerp(gaugeFillStartColor, gaugeFillEndColor, angle / twoPi);
                }

                pixels[y * size + x] = new Color32(
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        return texture;
    }

    /// <summary>가장자리만 부드럽게 깎은 흰색 원판. 중앙 버튼 배경에 쓴다.</summary>
    private Texture2D BuildDiscTexture(int size)
    {
        var texture = NewTexture(size, "SkipDisc");

        float center = (size - 1) * 0.5f;
        float radius = center;
        const float feather = 1.5f;

        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float dy = y - center;

            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = Mathf.Clamp01((radius - distance) / feather);

                pixels[y * size + x] =
                    new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, false);

        return texture;
    }

    private static Texture2D NewTexture(int size, string name)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
    }

    private static Sprite BuildSprite(Texture2D texture, string name)
    {
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);

        sprite.name = name;
        sprite.hideFlags = HideFlags.DontSave;

        return sprite;
    }

    /// <summary>
    /// 한글이 렌더링되는 TMP 폰트를 고른다.
    ///
    /// TMP 기본 폰트(LiberationSans)에는 한글 글리프가 없어 안내 문구가 빈 사각형이
    /// 된다. 그래서 인스펙터 지정을 먼저 쓰고, 비어 있으면 Opening 씬의 내레이션
    /// 텍스트가 쓰는 폰트를 그대로 가져온다. (읽기만 한다)
    /// </summary>
    private TMP_FontAsset ResolveFontAsset()
    {
        if (fontAsset != null)
            return fontAsset;

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font != null)
                    return text.font;
            }
        }

        Debug.LogWarning(
            "[Opening] 한글 TMP 폰트를 찾지 못했습니다. TMP 기본 폰트에는 한글 글리프가 " +
            "없어 안내 문구가 깨질 수 있습니다. OpeningSkipUI의 Font Asset에 " +
            "KoPub Dotum Medium SDF를 지정하세요.", this);

        return null;
    }
}
