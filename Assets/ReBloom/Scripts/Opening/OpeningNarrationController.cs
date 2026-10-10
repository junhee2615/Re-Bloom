using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Opening 첫 문장 연출.
///
/// 완전한 검정 화면에서 잠시 쉰 뒤 내레이션 한 줄을 부드럽게 띄우고 그대로 멈춘다.
/// 다음 문장이나 다른 씬으로 자동 진행하지 않는다.
///
/// 검정은 XR Origin 안의 기존 ScreenFade를 그대로 쓴다. ScreenFade.cs는 수정하지 않고,
/// 공개 API(StopAllCoroutines / FadeOut / enabled)만으로 검정 상태를 고정한다.
///
/// ScreenFade는 씬이 로드될 때마다 스스로 FadeIn(1f)을 시작하므로(ScreenFade.OnSceneLoaded),
/// 그대로 두면 검정이 걷히고 기본 스카이박스가 드러난다. 그래서
///   ① 진행 중인 자동 FadeIn을 끊고
///   ② FadeOut(0f)으로 알파를 1(완전 검정)에 고정하고
///   ③ 컴포넌트를 꺼서 이후 sceneLoaded에 다시 반응하지 않게 한다.
///
/// 내레이션 캔버스는 Screen Space - Overlay다. ScreenFadeCanvas가 Sorting Order
/// 32767(최댓값)인 월드 스페이스 캔버스라, 같은 월드 스페이스로 두면 어떤 값을 줘도
/// 검정 뒤에 가려진다. Overlay는 월드 렌더가 끝난 뒤 합성되므로 검정 위에 올라온다.
/// </summary>
public class OpeningNarrationController : MonoBehaviour
{
    [Header("Timing - 시작")]
    [Tooltip("검정 화면만 보이는 시간. 이 뒤에 첫 문장이 나타난다.")]
    [SerializeField, Min(0f)] private float initialDelay = 1f;

    [Header("Timing - 첫 번째 문장")]
    [SerializeField, Min(0f)] private float firstFadeInDuration = 1f;
    [SerializeField, Min(0f)] private float firstHoldDuration = 2.5f;
    [SerializeField, Min(0f)] private float firstFadeOutDuration = 0.8f;

    [Header("Timing - 문장 사이")]
    [Tooltip("첫 문장이 사라진 뒤 두 번째 문장이 나타나기까지의 검정 시간.")]
    [SerializeField, Min(0f)] private float betweenLinesDelay = 0.5f;

    [Header("Timing - 두 번째 문장")]
    [SerializeField, Min(0f)] private float secondFadeInDuration = 1f;
    [SerializeField, Min(0f)] private float secondHoldDuration = 3.5f;
    [SerializeField, Min(0f)] private float secondFadeOutDuration = 0.8f;

    [Header("Timing - 검정 마무리")]
    [Tooltip("두 번째 문장이 사라진 뒤 검정 화면을 유지하는 시간.")]
    [SerializeField, Min(0f)] private float endingBlackHoldDuration = 1f;

    [Header("Timing - 전환 문장 (\"그러나 지금,\")")]
    [SerializeField, Min(0f)] private float transitionLineFadeInDuration = 1f;
    [SerializeField, Min(0f)] private float transitionLineHoldDuration = 2f;
    [SerializeField, Min(0f)] private float transitionLineFadeOutDuration = 0.8f;

    [Header("Timing - Stage 환경 등장")]
    [Tooltip("검정이 걷히며 Stage 환경이 나타나는 시간.")]
    [SerializeField, Min(0f)] private float stage1FadeInDuration = 1.5f;

    [Header("Timing - Stage1 위 문장 (\"전기는 끊겼고,\")")]
    [SerializeField, Min(0f)] private float stage1LineFadeInDuration = 1f;
    [SerializeField, Min(0f)] private float stage1LineHoldDuration = 2.5f;
    [Tooltip("텍스트만 먼저 지운다. 환경은 아직 보인다.")]
    [SerializeField, Min(0f)] private float stage1LineFadeOutDuration = 0.8f;

    [Header("Timing - Stage1 → Stage2 전환")]
    [Tooltip("Stage1 환경이 검정으로 사라지는 시간.")]
    [SerializeField, Min(0f)] private float stage1FadeOutDuration = 1f;

    [Tooltip("검정이 걷히며 Stage2 환경이 나타나는 시간.")]
    [SerializeField, Min(0f)] private float stage2FadeInDuration = 1.5f;

    [Header("Timing - Stage2 위 문장")]
    [SerializeField, Min(0f)] private float stage2LineFadeInDuration = 1f;
    [SerializeField, Min(0f)] private float stage2LineHoldDuration = 3.5f;
    [Tooltip("텍스트만 먼저 지운다. 환경은 아직 보인다.")]
    [SerializeField, Min(0f)] private float stage2LineFadeOutDuration = 0.8f;

    [Header("Timing - Stage2 종료")]
    [Tooltip("Stage2 환경이 검정으로 사라지는 시간. 이후 Stage2를 언로드한다.")]
    [SerializeField, Min(0f)] private float stage2FadeOutDuration = 1f;

    [Header("Timing - 희망 내레이션")]
    [Tooltip("검정만 보이는 정적. 이 뒤에 첫 희망 문장이 나타난다.")]
    [SerializeField, Min(0f)] private float hopeInitialDelay = 1f;

    [SerializeField, Min(0f)] private float hopeLine1FadeInDuration = 1f;
    [SerializeField, Min(0f)] private float hopeLine1HoldDuration = 3f;
    [SerializeField, Min(0f)] private float hopeLine1FadeOutDuration = 0.8f;

    [SerializeField, Min(0f)] private float betweenHopeLinesDelay = 0.5f;

    [SerializeField, Min(0f)] private float hopeLine2FadeInDuration = 1f;
    [SerializeField, Min(0f)] private float hopeLine2HoldDuration = 3.5f;
    [SerializeField, Min(0f)] private float hopeLine2FadeOutDuration = 0.8f;

    [Header("Timing - Re:Bloom 타이틀")]
    [Tooltip("타이틀이 나타나기 전 검정만 보이는 시간.")]
    [SerializeField, Min(0f)] private float beforeTitleDelay = 1f;

    [SerializeField, Min(0f)] private float titleFadeInDuration = 1.5f;
    [SerializeField, Min(0f)] private float titleHoldDuration = 3f;

    [Tooltip("타이틀이 검정 속으로 사라지는 시간. 이 뒤에 세션이 시작된다.")]
    [SerializeField, Min(0f)] private float titleFadeOutDuration = 0.8f;

    [Header("Timing - 스킵")]
    [Tooltip("스킵할 때 현재 화면을 검정으로 덮는 시간. 정상 연출 타이밍과는 무관하다.")]
    [SerializeField, Min(0f)] private float skipFadeOutDuration = 0.6f;

    [Tooltip("스킵 시 진행 중인 Additive Load/Unload를 기다릴 최대 시간(초). " +
             "이 시간을 넘기면 스킵을 취소하고 오프닝을 계속 진행한다.")]
    [SerializeField, Min(1f)] private float skipSceneOperationTimeout = 30f;

    [Header("Font Size")]
    [Tooltip("내레이션 문장에 쓰는 크기. 모든 내레이션 구간에서 이 값으로 되돌린다.")]
    [SerializeField, Min(1f)] private float narrationFontSize = 36f;

    [Tooltip("타이틀에만 쓰는 크기.")]
    [SerializeField, Min(1f)] private float titleFontSize = 72f;

    [Header("Narration")]
    [SerializeField, TextArea(2, 4)]
    private string firstLine = "한때 이 도시에는 빛이 있었다.";

    [SerializeField, TextArea(2, 4)]
    private string secondLine = "물이 흘렀고,\n나무가 자랐고,\n사람들이 함께 숨쉬었다.";

    [SerializeField, TextArea(2, 4)]
    private string transitionLine = "그러나 지금,";

    [SerializeField, TextArea(2, 4)]
    private string stage1Line = "전기는 끊겼고,";

    [SerializeField, TextArea(2, 4)]
    private string stage2Line = "숲은 말라가고 있으며,\n물길은 막혔다.";

    [SerializeField, TextArea(2, 4)]
    private string hopeLine1 = "하지만 아직,\n모든 것이 사라진 것은 아니다.";

    [SerializeField, TextArea(2, 4)]
    private string hopeLine2 = "이 세계를 다시 피워낼\n두 개의 감각이 남아 있다.";

    [Tooltip("타이틀. 이 구간에서만 Font Size가 titleFontSize로 바뀐다.")]
    [SerializeField] private string titleLine = "Re:Bloom";

    [Header("References")]
    [Tooltip("페이드할 내레이션 텍스트의 CanvasGroup. (OpeningText)")]
    [SerializeField] private CanvasGroup narrationGroup;

    [Tooltip("문장을 바꿔 넣을 TMP. 비워 두면 narrationGroup과 같은 오브젝트에서 찾는다.")]
    [SerializeField] private TMP_Text narrationText;

    [Tooltip("Stage Additive Load / 정리 / 리그 이동 / 조명 전환을 담당한다. " +
             "비워 두면 같은 오브젝트에서 찾는다.")]
    [SerializeField] private OpeningEnvironmentController environmentController;

    [Tooltip("비워 두면 Opening 씬에서 자동으로 찾는다.")]
    [SerializeField] private ScreenFade screenFadeOverride;

    [Header("ScreenFadeCanvas 안에서의 텍스트 배치")]
    [Tooltip("ScreenFadeCanvas 기준 로컬 Z. 캔버스가 카메라 0.4m 앞이므로 1.6이면 눈에서 2.0m가 된다.")]
    [SerializeField] private float textDistance = 1.6f;

    [Tooltip("ScreenFadeCanvas는 localScale 1.4의 거대한 캔버스다. 그 안에서 한 줄 패널 크기로 줄이는 배율.")]
    [SerializeField] private float textLocalScale = 0.00167f;

    [Tooltip("텍스트 패널의 RectTransform 크기(캔버스 단위).")]
    [SerializeField] private Vector2 textPanelSize = new Vector2(600f, 150f);

    private ScreenFade screenFade;

    /// <summary>ScreenFade가 제어하는 검정 Image의 CanvasGroup. 알파를 읽기 위해서만 쓴다.</summary>
    private CanvasGroup screenFadeGroup;

    /// <summary>연출 본체. 스킵이 이것만 정지시킨다. (StopAllCoroutines는 쓰지 않는다)</summary>
    private Coroutine openingRoutine;

    /// <summary>스킵 처리 코루틴. 이 코루틴은 스킵에 의해 정지되지 않는다.</summary>
    private Coroutine skipRoutine;

    /// <summary>
    /// 종료 경로를 선점한 플래그. 정상 종료와 스킵이 동시에 실행되지 않게 하는 공통 게이트다.
    /// <see cref="TryClaimFinish"/> 만 이 값을 올린다.
    /// </summary>
    private bool finishing;

    /// <summary>이미 Opening 종료(정상/스킵)가 시작되었는지. 입력 스크립트가 참고한다.</summary>
    public bool IsFinishing => finishing;

    private void Start()
    {
        // 연출 본체를 코루틴으로 돌리고 핸들을 보관한다.
        // Start()를 IEnumerator로 두면 핸들이 없어 스킵이 StopAllCoroutines에 의존해야 하고,
        // 그러면 스킵 코루틴 자신까지 멈춘다. 시작 시점과 타이밍은 이전과 같다.
        openingRoutine = StartCoroutine(RunOpening());
    }

    private IEnumerator RunOpening()
    {
        // 참조는 여기서 한 번만 해석한다.
        screenFade = screenFadeOverride != null ? screenFadeOverride : ResolveScreenFade();

        if (narrationGroup == null)
            narrationGroup = ResolveNarrationGroup();

        // 텍스트는 투명하게 시작한다.
        if (narrationGroup != null)
        {
            narrationGroup.alpha = 0f;

            // 문장 교체용 TMP는 CanvasGroup과 같은 오브젝트에 있다.
            if (narrationText == null)
                narrationText = narrationGroup.GetComponent<TMP_Text>();
        }
        else
        {
            Debug.LogWarning("[Opening] 내레이션 CanvasGroup을 찾지 못했습니다.", this);
        }

        if (narrationText == null)
            Debug.LogWarning("[Opening] 내레이션 TMP를 찾지 못해 문장을 바꿀 수 없습니다.", this);

        if (environmentController == null)
            environmentController = GetComponent<OpeningEnvironmentController>();

        // ── 텍스트를 ScreenFadeCanvas 안으로 옮긴다 ──────────────
        AttachNarrationToScreenFadeCanvas();

        // 스킵이 "현재 알파에서" 검정으로 덮을 수 있도록 검정 CanvasGroup을 한 번만 찾아 둔다.
        screenFadeGroup = ResolveScreenFadeGroup();

        // ── 완전한 검정 고정 ─────────────────────────────────────
        if (screenFade != null)
        {
            screenFade.StopAllCoroutines();

            // FadeOut/FadeIn은 CanvasGroup만 건드리는 IEnumerator라 이쪽에서 돌려도 된다.
            // 0초를 주면 알파가 곧바로 1이 된다.
            yield return screenFade.FadeOut(0f);

            screenFade.enabled = false;
        }
        else
        {
            Debug.LogWarning(
                "[Opening] ScreenFade를 찾지 못해 검정 배경 없이 진행합니다.", this);
        }

        // ── 시작 여백 ────────────────────────────────────────────
        if (initialDelay > 0f)
            yield return new WaitForSeconds(initialDelay);

        // ── 첫 번째 문장 ─────────────────────────────────────────
        yield return ShowLine(
            firstLine, firstFadeInDuration, firstHoldDuration, firstFadeOutDuration);

        Debug.Log("[Opening] Narration line 1 complete");

        // ── 문장 사이 검정 ───────────────────────────────────────
        if (betweenLinesDelay > 0f)
            yield return new WaitForSeconds(betweenLinesDelay);

        // ── 두 번째 문장 ─────────────────────────────────────────
        yield return ShowLine(
            secondLine, secondFadeInDuration, secondHoldDuration, secondFadeOutDuration);

        Debug.Log("[Opening] Narration line 2 complete");

        // ── 마무리 검정 유지 ─────────────────────────────────────
        if (endingBlackHoldDuration > 0f)
            yield return new WaitForSeconds(endingBlackHoldDuration);

        Debug.Log("[Opening] Intro narration complete");

        // ── 전환 문장 "그러나 지금," (아직 검정) ─────────────────
        yield return ShowLine(
            transitionLine,
            transitionLineFadeInDuration,
            transitionLineHoldDuration,
            transitionLineFadeOutDuration);

        Debug.Log("[Opening] Transition line complete");

        // ── Stage1 환경을 검정 뒤에서 올린다 ─────────────────────
        if (environmentController != null)
            yield return environmentController.GoToStage(0);
        else
            Debug.LogWarning("[Opening] EnvironmentController가 없어 Stage를 올리지 못했습니다.", this);

        // ── 검정을 걷어 Stage 환경을 보여준다 ───────────────────
        // ScreenFade는 enabled=false 상태라 sceneLoaded 자동 FadeIn이 끼어들지 않는다.
        // FadeIn은 CanvasGroup만 건드리는 IEnumerator라 이쪽에서 돌린다.
        if (screenFade != null)
            yield return screenFade.FadeIn(stage1FadeInDuration);

        // ── Stage1 위에 올리는 문장 "전기는 끊겼고," ─────────────
        yield return ShowLine(
            stage1Line,
            stage1LineFadeInDuration,
            stage1LineHoldDuration,
            0f,
            keepVisible: true);

        Debug.Log("[Opening] Stage1 narration shown");

        // ── 텍스트만 먼저 지운다 (환경은 아직 보인다) ────────────
        yield return FadeNarration(1f, 0f, stage1LineFadeOutDuration);

        // ── Stage1 환경을 검정으로 덮는다 ────────────────────────
        if (screenFade != null)
            yield return screenFade.FadeOut(stage1FadeOutDuration);

        // ── 검정 뒤에서 Stage1 → Stage2 로 갈아끼운다 ────────────
        if (environmentController != null)
            yield return environmentController.GoToStage(1);
        else
            Debug.LogWarning("[Opening] EnvironmentController가 없어 Stage2로 넘어가지 못했습니다.", this);

        // ── 검정을 걷어 Stage2 환경을 보여준다 ──────────────────
        if (screenFade != null)
            yield return screenFade.FadeIn(stage2FadeInDuration);

        // ── Stage2 위에 올리는 문장 ──────────────────────────────
        yield return ShowLine(
            stage2Line,
            stage2LineFadeInDuration,
            stage2LineHoldDuration,
            0f,
            keepVisible: true);

        Debug.Log("[Opening] Stage2 narration shown");

        // ── 텍스트만 먼저 지운다 (환경은 아직 보인다) ────────────
        yield return FadeNarration(1f, 0f, stage2LineFadeOutDuration);

        Debug.Log("[Opening] Stage2 narration complete");

        // ── Stage2 환경을 검정으로 덮는다 ────────────────────────
        if (screenFade != null)
            yield return screenFade.FadeOut(stage2FadeOutDuration);

        // ── 검정 뒤에서 Stage2를 내린다. 다음 Stage는 올리지 않는다 ──
        if (environmentController != null)
            yield return environmentController.UnloadStage();

        // ── 정적 ─────────────────────────────────────────────────
        if (hopeInitialDelay > 0f)
            yield return new WaitForSeconds(hopeInitialDelay);

        // ── 희망 문장 1 ──────────────────────────────────────────
        yield return ShowLine(
            hopeLine1,
            hopeLine1FadeInDuration,
            hopeLine1HoldDuration,
            hopeLine1FadeOutDuration);

        Debug.Log("[Opening] Hope line 1 complete");

        // ── 문장 사이 검정 ───────────────────────────────────────
        if (betweenHopeLinesDelay > 0f)
            yield return new WaitForSeconds(betweenHopeLinesDelay);

        // ── 희망 문장 2 ──────────────────────────────────────────
        yield return ShowLine(
            hopeLine2,
            hopeLine2FadeInDuration,
            hopeLine2HoldDuration,
            hopeLine2FadeOutDuration);

        Debug.Log("[Opening] Hope line 2 complete");

        // ── 타이틀 전 검정 ───────────────────────────────────────
        if (beforeTitleDelay > 0f)
            yield return new WaitForSeconds(beforeTitleDelay);

        // ── Re:Bloom 타이틀 ──────────────────────────────────────
        // 이 구간에서만 Font Size를 titleFontSize로 올린다.
        // Hold가 끝나면 검정 속으로 사라지고, 그 뒤에 Opening을 정리하고 세션을 시작한다.
        yield return ShowLine(
            titleLine,
            titleFadeInDuration,
            titleHoldDuration,
            titleFadeOutDuration,
            fontSizeOverride: titleFontSize);

        Debug.Log("[Opening] Title complete");

        // ── Opening 정리 후 기존 세션 흐름으로 넘긴다 ────────────
        yield return FinishOpeningAndEnterSession();
    }

    /// <summary>
    /// 타이틀이 완전히 사라진 뒤의 Opening 마무리.
    ///
    /// 순서가 중요하다 —
    ///   ① 세션 요청 검증 (실패하면 아무것도 정리하지 않고 멈춘다)
    ///   ② 리그 포즈 복구   : Lobby에는 리그를 배치하는 코드가 없다
    ///   ③ OpeningText 제거 : DontDestroyOnLoad 영역에 있어 씬이 사라져도 남는다
    ///   ④ ScreenFade 복구  : Lobby가 기존 방식으로 Fade In할 수 있게
    ///   ⑤ EnterSession
    ///
    /// Locomotion은 여기서 켜지 않는다. 자세한 이유는
    /// <see cref="OpeningEnvironmentController.RestoreLocomotionForExit"/> 주석에 있다.
    /// </summary>
    private IEnumerator FinishOpeningAndEnterSession()
    {
        if (!TryClaimFinish())
        {
            // 스킵이 먼저 종료를 선점했다. 같은 정리를 두 번 하지 않는다.
            Debug.Log("[Opening] Finish already claimed - 정상 종료를 건너뜁니다.", this);
            yield break;
        }

        yield return FinishOpeningCore();
    }

    /// <summary>
    /// 종료 경로를 선점한다. 정상 종료와 스킵이 공유하는 게이트다.
    /// 처음 부른 쪽만 true를 받고, 그 뒤의 호출은 모두 false가 된다.
    ///
    /// 메인 스레드에서만 호출되므로(코루틴 / Update) lock은 필요하지 않다.
    /// </summary>
    private bool TryClaimFinish()
    {
        if (finishing)
            return false;

        finishing = true;
        return true;
    }

    /// <summary>
    /// 실제 마무리. 정상 종료와 스킵이 **같은 구현**을 쓴다.
    /// 게이트는 호출하는 쪽에서 이미 통과한 상태로 들어온다.
    /// </summary>
    private IEnumerator FinishOpeningCore()
    {
        // ── ① 세션 요청 검증 ────────────────────────────────────
        if (!OpeningSessionRequest.HasRequest)
        {
            Debug.LogError(
                "[Opening] 세션 요청이 없습니다. StartScene의 Single/Multi 버튼을 거치지 않고 " +
                "Opening을 직접 실행하면 이 상태가 됩니다. EnterSession을 호출하지 않고 멈춥니다.", this);
            yield break;
        }

        if (NetworkManager.Instance == null)
        {
            Debug.LogError(
                "[Opening] NetworkManager.Instance가 없습니다. StartScene의 Manager가 " +
                "DontDestroyOnLoad로 넘어왔는지 확인하세요. EnterSession을 호출하지 않고 멈춥니다.", this);
            yield break;
        }

        // 값을 먼저 로컬로 복사한다. Clear는 입장 성공을 확인한 뒤에만 한다.
        string roomCode = OpeningSessionRequest.RoomCode;
        SessionMode mode = OpeningSessionRequest.Mode;

        // ── ② 리그 포즈 복구 ────────────────────────────────────
        if (environmentController != null)
            environmentController.RestoreRigPoseForExit();

        // ── ③ OpeningText 제거 ─────────────────────────────────
        DestroyNarrationText();

        // ── ④ ScreenFade 복구 ──────────────────────────────────
        yield return RestoreScreenFadeForLobby();

        // ── ⑤ 세션 시작 ────────────────────────────────────────
        Debug.Log($"[Opening] Session start - mode={mode}, room={roomCode}");

        EnterSessionAndClearOnSuccess(roomCode, mode);
    }

    // ── 스킵 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 오프닝 스킵을 요청한다. 입력 판정은 <see cref="OpeningSkipInput"/> 가 담당하고
    /// 여기서는 "안전하게 끝내는 순서"만 책임진다.
    ///
    /// 몇 번 불러도 안전하다 — 이미 스킵이 돌고 있거나 종료가 선점되어 있으면 무시한다.
    /// </summary>
    public void RequestSkip()
    {
        if (skipRoutine != null)
            return;

        if (finishing)
        {
            Debug.Log("[Opening] Skip ignored - 종료가 이미 진행 중입니다.", this);
            return;
        }

        skipRoutine = StartCoroutine(SkipOpeningRoutine());
    }

    /// <summary>
    /// 스킵 처리.
    ///
    /// 순서가 중요하다 —
    ///   ① 세션 요청 검증  : Lobby로 갈 수 없으면 연출을 끊지 않고 그대로 둔다
    ///   ② 씬 작업 대기    : 진행 중인 Additive Load/Unload의 후처리를 보장한다
    ///   ③ 종료 선점       : 이 뒤로 정상 종료 경로는 들어오지 못한다
    ///   ④ 연출 코루틴 정지 : ②를 지났으므로 비동기 씬 작업 중이 아니다
    ///   ⑤ 검정으로 덮기   : 현재 알파에서 시작하므로 검정 구간에서도 깜빡이지 않는다
    ///   ⑥ Stage 언로드    : 정상 종료와 같은 UnloadStage 경로를 쓴다
    ///   ⑦ Main Camera 태그 복구 (안전망)
    ///   ⑧ FinishOpeningCore : 정상 종료와 완전히 같은 마무리
    ///
    /// ②를 먼저 두는 이유:
    /// LoadSceneAsync 도중에 연출 코루틴을 멈추면 로드 자체는 계속 진행되는데
    /// 그 뒤의 정리 / Active Scene 전환 / 리그 이동 / Main Camera 태그 복구는 영원히
    /// 실행되지 않는다. 그래서 StopAllCoroutines로 끊고 바로 씬을 넘기지 않고,
    /// 씬 작업이 스스로 끝나는 것을 기다린 뒤에만 연출을 정지한다.
    ///
    /// Locomotion은 여기서도 켜지 않는다. 정상 종료와 같은 이유로
    /// <c>OpeningEnvironmentController.OnDestroy</c> 의 복구에 맡긴다.
    /// </summary>
    private IEnumerator SkipOpeningRoutine()
    {
        Debug.Log("[Opening] Skip start", this);

        // ── ① 세션 요청 검증 ────────────────────────────────────
        // 여기서 실패하면 스킵해도 Lobby로 갈 수 없다. 연출을 끊지 않고 그대로 둔다.
        if (!OpeningSessionRequest.HasRequest || NetworkManager.Instance == null)
        {
            Debug.LogError(
                "[Opening] 세션 요청 또는 NetworkManager가 없어 스킵을 취소합니다. " +
                "오프닝을 그대로 계속 진행합니다.", this);

            skipRoutine = null;
            yield break;
        }

        // ── ② 진행 중인 비동기 씬 작업 대기 ─────────────────────
        if (environmentController != null && environmentController.IsSceneOperationInProgress)
        {
            Debug.Log("[Opening] Skip waiting for scene operation", this);

            float waited = 0f;

            while (environmentController.IsSceneOperationInProgress &&
                   waited < skipSceneOperationTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (environmentController.IsSceneOperationInProgress)
            {
                Debug.LogError(
                    $"[Opening] 씬 작업이 {skipSceneOperationTimeout}초 안에 끝나지 않아 " +
                    "스킵을 취소합니다. 오프닝을 그대로 계속 진행합니다.", this);

                skipRoutine = null;
                yield break;
            }

            Debug.Log($"[Opening] Skip scene operation finished ({waited:F2}s)", this);
        }

        // ── ③ 종료 선점 ────────────────────────────────────────
        // ②를 기다리는 동안 연출이 정상 종료에 도달했을 수 있다. 그때는 스킵이 물러난다.
        if (!TryClaimFinish())
        {
            Debug.Log("[Opening] Skip ignored - 정상 종료가 먼저 시작되었습니다.", this);

            skipRoutine = null;
            yield break;
        }

        // ── ④ 연출 코루틴 정지 ─────────────────────────────────
        if (openingRoutine != null)
        {
            StopCoroutine(openingRoutine);
            openingRoutine = null;

            Debug.Log("[Opening] Narration routine stopped", this);
        }

        // ── ⑤ 검정으로 덮기 ────────────────────────────────────
        yield return FadeOutForSkip(skipFadeOutDuration);

        // ── ⑥ 올라가 있는 Stage 언로드 ─────────────────────────
        if (environmentController != null && environmentController.HasStageLoaded)
            yield return environmentController.UnloadStage();

        // ── ⑦ Main Camera 태그 복구 ────────────────────────────
        if (environmentController != null)
            environmentController.RestoreMainCameraTagForExit();

        // ── ⑧ 정상 종료와 같은 마무리 ──────────────────────────
        yield return FinishOpeningCore();

        skipRoutine = null;

        Debug.Log("[Opening] Skip complete");
    }

    /// <summary>
    /// 스킵용 페이드. 검정과 내레이션 텍스트를 한 루프에서 함께 처리한다.
    ///
    /// <see cref="ScreenFade.FadeOut"/> 를 쓰지 않는 이유:
    /// 그 구현은 현재 알파와 무관하게 항상 0 → 1로 보간한다. 이미 검정인 구간(내레이션
    /// 사이, 타이틀 전후)에서 스킵하면 첫 프레임에 알파가 0으로 떨어져 Stage가 번쩍인다.
    /// 그래서 ScreenFade.cs는 수정하지 않고, 같은 CanvasGroup의 현재 알파에서 시작한다.
    ///
    /// 코루틴을 따로 띄우지 않고 한 루프에서 두 알파를 함께 움직이는 이유:
    /// 텍스트 페이드를 별도 코루틴으로 돌리면 뒤이은 <see cref="DestroyNarrationText"/> 와
    /// 수명이 겹칠 수 있다.
    /// </summary>
    private IEnumerator FadeOutForSkip(float duration)
    {
        // 알파를 읽을 CanvasGroup을 못 찾았을 때의 안전한 대안.
        // FadeOut(0f)은 while 조건이 바로 거짓이 되어 보간 없이 알파 1이 되므로 깜빡임이 없다.
        if (screenFadeGroup == null)
        {
            if (narrationGroup != null)
                narrationGroup.alpha = 0f;

            if (screenFade != null)
                yield return screenFade.FadeOut(0f);

            yield break;
        }

        float blackFrom = screenFadeGroup.alpha;
        float textFrom = narrationGroup != null ? narrationGroup.alpha : 0f;

        if (duration > 0f && (blackFrom < 1f || textFrom > 0f))
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                float t = Mathf.Clamp01(elapsed / duration);

                screenFadeGroup.alpha = Mathf.Lerp(blackFrom, 1f, t);

                if (narrationGroup != null)
                    narrationGroup.alpha = Mathf.Lerp(textFrom, 0f, t);

                yield return null;
            }
        }

        screenFadeGroup.alpha = 1f;

        if (narrationGroup != null)
            narrationGroup.alpha = 0f;
    }

    /// <summary>
    /// ScreenFade가 제어하는 검정 Image의 CanvasGroup을 찾는다. 알파를 읽기 위해서만 쓴다.
    ///
    /// ScreenFade.canvasGroup은 private 직렬화 필드라 밖에서 읽을 수 없고,
    /// ScreenFade.cs는 수정 대상이 아니다. ScreenFadeCanvas 프리팹에서 ScreenFade가
    /// 가리키는 것은 **자식 Image**의 CanvasGroup이고, 루트에도 별개의 CanvasGroup이
    /// 하나 더 있다. 그래서 루트와 내레이션 그룹을 제외한 첫 번째를 쓴다.
    ///
    /// 못 찾으면 null을 돌려주고 <see cref="FadeOutForSkip"/> 가 즉시 검정으로 대체한다.
    /// </summary>
    private CanvasGroup ResolveScreenFadeGroup()
    {
        if (screenFade == null)
            return null;

        foreach (CanvasGroup group in screenFade.GetComponentsInChildren<CanvasGroup>(true))
        {
            if (group.gameObject == screenFade.gameObject)
                continue;

            if (group == narrationGroup)
                continue;

            return group;
        }

        Debug.LogWarning(
            "[Opening] ScreenFade의 검정 CanvasGroup을 찾지 못했습니다. " +
            "스킵 시 페이드 없이 즉시 검정으로 전환됩니다.", this);

        return null;
    }

    /// <summary>
    /// OpeningText를 치운다.
    ///
    /// 이 오브젝트는 <see cref="AttachNarrationToScreenFadeCanvas"/> 에서 영속
    /// ScreenFadeCanvas의 자식으로 옮겨졌다. 그래서 Opening 씬이 언로드돼도 함께
    /// 사라지지 않고, 그대로 두면 Lobby에서 "Re:Bloom" 글자가 계속 떠 있다.
    ///
    /// Destroy는 프레임 끝에 처리되므로 알파를 먼저 0으로 만들어 그 사이에도 보이지 않게 한다.
    /// 새 Text는 만들지 않는다.
    /// </summary>
    private void DestroyNarrationText()
    {
        if (narrationGroup == null)
            return;

        narrationGroup.alpha = 0f;

        GameObject textObject = narrationGroup.gameObject;

        narrationGroup = null;
        narrationText = null;

        Destroy(textObject);

        Debug.Log("[Opening] OpeningText destroyed");
    }

    /// <summary>
    /// ScreenFade를 기존 동작으로 되돌린다. ScreenFade.cs는 수정하지 않고
    /// 공개 API(FadeOut / enabled)만 쓴다.
    ///
    /// ScreenFade는 OnEnable에서 sceneLoaded를 구독하고 OnDisable에서 해제하며,
    /// sceneLoaded가 오면 스스로 FadeIn(1f)을 돌린다. Opening 시작 시 enabled=false로
    /// 꺼 둔 상태이므로 지금은 구독이 끊겨 있다.
    ///
    /// 그래서 순서를 이렇게 둔다 —
    ///   ① FadeOut(0f)로 알파를 1(완전 검정)에 맞춘다. while 조건이 바로 거짓이 되어
    ///      즉시 1이 된다. 이걸 먼저 해 두어야 Lobby의 자동 FadeIn이 1 → 0으로 온전히 돈다.
    ///   ② enabled = true 로 구독을 되살린다.
    ///
    /// 이 뒤에 로드되는 첫 씬은 Fusion이 올리는 Lobby뿐이므로, Lobby 로드가 끝나는
    /// 시점에 ScreenFade가 기존 방식 그대로 Fade In한다.
    /// 여기서 수동 FadeIn을 돌리지 않기 때문에 자동 FadeIn과 겹치지 않는다.
    /// </summary>
    private IEnumerator RestoreScreenFadeForLobby()
    {
        if (screenFade == null)
        {
            Debug.LogWarning(
                "[Opening] ScreenFade를 찾지 못해 복구를 건너뜁니다. " +
                "Lobby가 검정으로 남을 수 있습니다.", this);
            yield break;
        }

        yield return screenFade.FadeOut(0f);

        screenFade.enabled = true;

        Debug.Log("[Opening] ScreenFade restored");
    }

    /// <summary>
    /// 기존 <see cref="NetworkManager.EnterSession"/> 을 그대로 호출한다.
    /// 세션의 첫 Network Scene은 NetworkManager가 정하는 Lobby 그대로다.
    /// Single / Multi 분기는 만들지 않고 보관된 Mode를 그대로 넘긴다.
    ///
    /// 코루틴이 아니라 async void인 이유:
    /// EnterSession의 Task는 Lobby 로드가 끝난 뒤에 완료된다. 그 시점에 Opening은 이미
    /// 언로드되어 이 컴포넌트가 파괴돼 있으므로 코루틴으로는 결과를 받을 수 없다.
    /// async void의 연속은 Unity 메인 스레드 컨텍스트에 남아 계속 실행되므로,
    /// 파괴 뒤에도 성공 여부를 보고 Clear할 수 있다.
    /// 그래서 이 뒤의 로그에는 컨텍스트(this)를 넘기지 않는다.
    /// </summary>
    private async void EnterSessionAndClearOnSuccess(string roomCode, SessionMode mode)
    {
        bool entered = await NetworkManager.Instance.EnterSession(roomCode, mode);

        if (entered)
        {
            // 성공한 뒤에만 비운다. EnterSession은 실패 시 _sessionStarted를 되돌려
            // 재시도를 허용하므로, 먼저 비우면 재시도에 쓸 room/mode가 사라진다.
            OpeningSessionRequest.Clear();

            Debug.Log("[Opening] Session request cleared");
            return;
        }

        Debug.LogError(
            $"[Opening] 세션 입장에 실패했습니다 (room={roomCode}, mode={mode}). " +
            "재시도를 위해 OpeningSessionRequest 값을 유지합니다.");

        // 실패하면 어떤 씬도 로드되지 않아 ScreenFade의 자동 FadeIn이 돌지 않는다.
        // 검정 그대로 두면 멈춘 것과 구분되지 않으므로 수동으로 한 번만 걷는다.
        // (자동 FadeIn은 sceneLoaded에서만 돌기 때문에 겹치지 않는다.)
        if (screenFade != null)
            screenFade.StartCoroutine(screenFade.FadeIn(1f));
    }
    /// <summary>
    /// 문장 하나를 띄우고 유지한 뒤 지운다. OpeningText 하나를 계속 재사용하며
    /// 알파가 0인 상태에서만 내용을 바꿔 글자가 바뀌는 순간이 보이지 않게 한다.
    /// </summary>
    /// <param name="keepVisible">true면 Fade Out 없이 보이는 상태로 둔다.</param>
    /// <param name="fontSizeOverride">0보다 크면 이 크기를 쓴다. 그 외에는 항상 narrationFontSize로 돌아간다.</param>
    private IEnumerator ShowLine(
        string line, float fadeIn, float hold, float fadeOut,
        bool keepVisible = false, float fontSizeOverride = 0f)
    {
        if (narrationGroup != null)
            narrationGroup.alpha = 0f;

        if (narrationText != null)
        {
            // 알파가 0인 상태에서만 크기와 내용을 바꾼다. 바뀌는 순간이 보이지 않는다.
            narrationText.fontSize =
                fontSizeOverride > 0f ? fontSizeOverride : narrationFontSize;

            narrationText.text = line;
        }

        yield return FadeNarration(0f, 1f, fadeIn);

        if (hold > 0f)
            yield return new WaitForSeconds(hold);

        if (keepVisible)
            yield break;

        yield return FadeNarration(1f, 0f, fadeOut);
    }

    /// <summary>
    /// 내레이션 텍스트를 ScreenFadeCanvas의 마지막 자식으로 옮긴다.
    ///
    /// Screen Space 캔버스는 HMD 눈 텍스처에 합성되지 않으므로 VR에서는 World Space만 쓸 수 있다.
    /// 그런데 ScreenFadeCanvas는 Sorting Order 32767(상한)을 점유한 World Space 캔버스라,
    /// 별도 캔버스로는 그 검정을 이길 수 없다. 그래서 '이기는' 대신 같은 캔버스 안으로 들어간다.
    /// 같은 캔버스에서는 하이어라키 순서가 드로우 순서이므로 마지막 자식이 검정 위에 그려진다.
    ///
    /// ScreenFade는 루트가 아니라 자식 Image의 CanvasGroup만 제어하므로,
    /// 형제로 들어온 이 텍스트는 검정 알파의 영향을 받지 않는다.
    ///
    /// ScreenFadeCanvas 프리팹과 XR Origin 프리팹은 읽기만 한다. 부모만 런타임에 바뀌며,
    /// Opening의 VRSystem은 비영속이라 씬이 끝나면 함께 사라진다.
    /// </summary>
    private void AttachNarrationToScreenFadeCanvas()
    {
        if (screenFade == null || narrationGroup == null)
            return;

        // ScreenFade는 ScreenFadeCanvas 루트에 붙어 있고, 그 오브젝트가 Canvas도 가진다.
        Canvas fadeCanvas = screenFade.GetComponent<Canvas>();

        if (fadeCanvas == null)
        {
            Debug.LogWarning(
                "[Opening] ScreenFade가 붙은 오브젝트에서 Canvas를 찾지 못해 " +
                "텍스트를 옮기지 않았습니다. VR에서 보이지 않을 수 있습니다.", this);
            return;
        }

        RectTransform textRect = narrationGroup.transform as RectTransform;

        if (textRect == null)
        {
            Debug.LogWarning("[Opening] 내레이션 오브젝트에 RectTransform이 없습니다.", this);
            return;
        }

        textRect.SetParent(fadeCanvas.transform, false);
        textRect.SetAsLastSibling();   // 검정 Image 뒤 순서 = 검정 위에 그려짐

        textRect.localRotation = Quaternion.identity;
        textRect.localScale = new Vector3(textLocalScale, textLocalScale, textLocalScale);

        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = textPanelSize;

        // anchoredPosition이 localPosition의 x·y를 덮으므로 z는 마지막에 넣는다.
        textRect.localPosition = new Vector3(0f, 0f, textDistance);

        Debug.Log("[Opening] Narration attached to ScreenFadeCanvas");
    }

    private IEnumerator FadeNarration(float from, float to, float duration)
    {
        if (narrationGroup == null)
            yield break;

        if (duration <= 0f)
        {
            narrationGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            narrationGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        narrationGroup.alpha = to;
    }

    /// <summary>
    /// ScreenFade는 XR Origin 프리팹 안(ScreenFadeCanvas)에 들어 있다.
    ///
    /// Opening 씬에는 더 이상 VRSystem이 없고, 리그는 StartScene에서
    /// DontDestroyOnLoad로 넘어온다. 그래서 Opening 씬 범위가 아니라
    /// 로드된 모든 씬(+ DontDestroyOnLoad 영역)을 대상으로 찾아야 한다.
    ///
    /// Start()에서 한 번만 호출한다. 매 프레임 탐색하지 않는다.
    /// </summary>
    private ScreenFade ResolveScreenFade()
    {
        return FindAnyObjectByType<ScreenFade>(FindObjectsInactive.Include);
    }

    /// <summary>인스펙터 연결이 비었을 때를 위한 폴백. Opening 씬에서만 찾는다.</summary>
    private CanvasGroup ResolveNarrationGroup()
    {
        // OpeningText는 Opening 씬 소유이므로 여기는 전역으로 넓히지 않는다.
        // (리그 쪽 ScreenFadeCanvas의 CanvasGroup을 잡으면 안 된다.)
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            // ScreenFade의 CanvasGroup을 잡지 않도록 ScreenFade가 달린 계층은 건너뛴다.
            if (root.GetComponentInChildren<ScreenFade>(true) != null)
                continue;

            CanvasGroup group = root.GetComponentInChildren<CanvasGroup>(true);

            if (group != null)
                return group;
        }

        return null;
    }
}
