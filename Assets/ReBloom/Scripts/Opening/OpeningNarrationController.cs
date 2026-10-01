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

    [Header("Timing - Stage 위 문장 (\"전기는 끊겼고,\")")]
    [SerializeField, Min(0f)] private float stage1LineFadeInDuration = 1f;
    [SerializeField, Min(0f)] private float stage1LineHoldDuration = 2.5f;

    [Header("Narration")]
    [SerializeField, TextArea(2, 4)]
    private string firstLine = "한때 이 도시에는 빛이 있었다.";

    [SerializeField, TextArea(2, 4)]
    private string secondLine = "물이 흘렀고,\n나무가 자랐고,\n사람들이 함께 숨쉬었다.";

    [SerializeField, TextArea(2, 4)]
    private string transitionLine = "그러나 지금,";

    [SerializeField, TextArea(2, 4)]
    private string stage1Line = "전기는 끊겼고,";

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

    private IEnumerator Start()
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

        // ── Stage 환경을 검정 뒤에서 갈아끼운다 ──────────────────
        if (environmentController != null)
            yield return environmentController.LoadStageBlind();
        else
            Debug.LogWarning("[Opening] EnvironmentController가 없어 Stage를 올리지 못했습니다.", this);

        // ── 검정을 걷어 Stage 환경을 보여준다 ───────────────────
        // ScreenFade는 enabled=false 상태라 sceneLoaded 자동 FadeIn이 끼어들지 않는다.
        // FadeIn은 CanvasGroup만 건드리는 IEnumerator라 이쪽에서 돌린다.
        if (screenFade != null)
            yield return screenFade.FadeIn(stage1FadeInDuration);

        // ── Stage 위에 올리는 문장 "전기는 끊겼고," ──────────────
        // 이번 단계에서는 Fade Out하지 않고 보이는 상태로 멈춘다.
        yield return ShowLine(
            stage1Line,
            stage1LineFadeInDuration,
            stage1LineHoldDuration,
            0f,
            keepVisible: true);

        // ── 이후 아무것도 하지 않는다 ────────────────────────────
        // 다음 단계에서 여기에 "숲은 말라가고 있으며" → Stage2 를 잇는다.
        Debug.Log("[Opening] Stage1 narration shown");
    }

    /// <summary>
    /// 문장 하나를 띄우고 유지한 뒤 지운다. OpeningText 하나를 계속 재사용하며
    /// 알파가 0인 상태에서만 내용을 바꿔 글자가 바뀌는 순간이 보이지 않게 한다.
    /// </summary>
    /// <param name="keepVisible">true면 Fade Out 없이 보이는 상태로 둔다.</param>
    private IEnumerator ShowLine(
        string line, float fadeIn, float hold, float fadeOut, bool keepVisible = false)
    {
        if (narrationGroup != null)
            narrationGroup.alpha = 0f;

        if (narrationText != null)
            narrationText.text = line;

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

    /// <summary>ScreenFade는 XR Origin 프리팹 안(ScreenFadeCanvas)에 들어 있다.</summary>
    private ScreenFade ResolveScreenFade()
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            ScreenFade fade = root.GetComponentInChildren<ScreenFade>(true);

            if (fade != null)
                return fade;
        }

        return null;
    }

    /// <summary>인스펙터 연결이 비었을 때를 위한 폴백. Opening 씬에서만 찾는다.</summary>
    private CanvasGroup ResolveNarrationGroup()
    {
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
