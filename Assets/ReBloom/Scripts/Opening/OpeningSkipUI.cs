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
/// 영속 캔버스 아래에 들어가므로 Opening이 끝나면 반드시 지워야 한다.
/// 그대로 두면 Lobby에서 안내 문구가 계속 떠 있다.
/// </summary>
public class OpeningSkipUI : MonoBehaviour
{
    [Header("안내 문구")]
    [SerializeField, TextArea(1, 3)]
    private string hintText = "Y 버튼을 3초간 길게 눌러 스킵";

    [SerializeField, Min(0f)] private float hintFadeInDuration = 0.6f;

    [Tooltip("완전히 보인 상태로 유지하는 시간. 이 뒤에 페이드아웃된다.")]
    [SerializeField, Min(0f)] private float hintHoldDuration = 5f;

    [SerializeField, Min(0f)] private float hintFadeOutDuration = 0.8f;
    [SerializeField, Min(1f)] private float hintFontSize = 28f;
    [SerializeField] private Color hintColor = new Color(0.86f, 0.88f, 0.86f, 1f);

    [Header("원형 홀드 게이지")]
    [Tooltip("채워지지 않은 기본 링. 어두운 회색.")]
    [SerializeField] private Color gaugeTrackColor = new Color(0.16f, 0.17f, 0.18f, 0.85f);

    [Tooltip("진행 링. 채도 낮은 연녹색 #91BFA0.")]
    [SerializeField] private Color gaugeFillColor = new Color(0.5686275f, 0.7490196f, 0.627451f, 1f);

    [SerializeField, Min(10f)] private float gaugeSize = 150f;

    [Tooltip("링 두께. 지름 대비 비율이다.")]
    [SerializeField, Range(0.05f, 0.45f)] private float gaugeThickness = 0.16f;

    [SerializeField] private string gaugeLabel = "Y";
    [SerializeField, Min(1f)] private float gaugeLabelFontSize = 54f;
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

    [Tooltip("링 스프라이트를 직접 지정할 때만 쓴다. 비워 두면 런타임에 만든다.")]
    [SerializeField] private Sprite ringSpriteOverride;

    [Tooltip("런타임 링 스프라이트의 한 변 픽셀 수.")]
    [SerializeField, Min(32)] private int ringResolution = 256;

    // 런타임에 만든 것들. 전부 이 스크립트가 책임지고 지운다.
    private RectTransform uiRoot;
    private GameObject gaugeRoot;
    private Image gaugeFill;
    private CanvasGroup hintGroup;
    private Sprite generatedRingSprite;
    private Texture2D generatedRingTexture;

    private bool cleanedUp;

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

        // 정상 종료든 스킵이든 종료가 시작되면 즉시 치운다.
        // ScreenFadeCanvas는 영속이므로 남겨 두면 Lobby까지 따라간다.
        if (narrationController != null && narrationController.IsFinishing)
        {
            CleanupUI("finishing");
            return;
        }

        if (gaugeRoot == null || skipInput == null)
            return;

        bool holding = skipInput.IsHolding;

        if (gaugeRoot.activeSelf != holding)
            gaugeRoot.SetActive(holding);

        // 놓으면 HoldProgress01이 0이 되므로 별도 초기화가 필요하지 않다.
        if (holding && gaugeFill != null)
            gaugeFill.fillAmount = skipInput.HoldProgress01;
    }

    private void OnDestroy()
    {
        CleanupUI("destroy");
    }

    // ── UI 생성 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ScreenFadeCanvas 아래에 안내 문구와 게이지를 만든다.
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

        // ── 루트 ────────────────────────────────────────────────
        uiRoot = CreateRect("OpeningSkipUI", fadeCanvas.transform, layer);

        uiRoot.localScale = new Vector3(uiLocalScale, uiLocalScale, uiLocalScale);
        uiRoot.sizeDelta = Vector2.zero;

        // anchoredPosition이 localPosition의 x·y를 덮으므로 z는 마지막에 넣는다.
        uiRoot.localPosition = new Vector3(0f, 0f, uiDistance);

        PlaceAboveBlackImage(uiRoot, screenFade);

        // ── 게이지 ──────────────────────────────────────────────
        Sprite ring = ringSpriteOverride != null ? ringSpriteOverride : CreateRingSprite();

        RectTransform gauge = CreateRect("SkipGauge", uiRoot, layer);
        gauge.anchoredPosition = gaugeAnchoredPosition;
        gauge.sizeDelta = new Vector2(gaugeSize, gaugeSize);

        gaugeRoot = gauge.gameObject;

        CreateRingImage("GaugeTrack", gauge, layer, ring, gaugeTrackColor, filled: false);
        gaugeFill = CreateRingImage("GaugeFill", gauge, layer, ring, gaugeFillColor, filled: true);

        TextMeshProUGUI label = CreateText(
            "GaugeLabel", gauge, layer, gaugeLabel, gaugeLabelFontSize, gaugeLabelColor);

        label.rectTransform.sizeDelta = new Vector2(gaugeSize, gaugeSize);

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
        gaugeFill = null;
        hintGroup = null;

        if (uiRoot != null)
        {
            Destroy(uiRoot.gameObject);
            uiRoot = null;
        }

        // 런타임에 만든 스프라이트/텍스처는 씬 언로드로 회수되지 않는다.
        if (generatedRingSprite != null)
        {
            Destroy(generatedRingSprite);
            generatedRingSprite = null;
        }

        if (generatedRingTexture != null)
        {
            Destroy(generatedRingTexture);
            generatedRingTexture = null;
        }

        Debug.Log($"[Opening] Skip UI cleaned up ({reason})");
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

    /// <summary>링 Image 하나. filled면 Radial 360 / Top / Clockwise로 둔다.</summary>
    private Image CreateRingImage(
        string name, Transform parent, int layer, Sprite ring, Color color, bool filled)
    {
        RectTransform rect = CreateRect(name, parent, layer);

        // 부모(게이지) 크기를 그대로 채운다.
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.sizeDelta = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;

        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = ring;
        image.color = color;

        // UI Ray 입력을 차단하지 않는다.
        image.raycastTarget = false;

        if (filled)
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = true;
            image.fillAmount = 0f;
        }
        else
        {
            image.type = Image.Type.Simple;
        }

        return image;
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

    /// <summary>
    /// 링(도넛) 스프라이트를 런타임에 만든다.
    ///
    /// Shader / RenderTexture를 쓰지 않는다 — 알파만 가진 평범한 RGBA32 텍스처라
    /// 기본 UI 머티리얼로 그려지고 Quest Single Pass Instanced에서도 그대로 동작한다.
    /// Filled 이미지는 Tight 메시에서 어긋나므로 FullRect로 만든다.
    /// </summary>
    private Sprite CreateRingSprite()
    {
        int size = Mathf.Max(32, ringResolution);

        generatedRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "OpeningSkipRing",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };

        float center = (size - 1) * 0.5f;
        float outer = center;
        float inner = Mathf.Max(1f, outer - gaugeThickness * size);
        const float feather = 1.5f;

        var pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            float dy = y - center;

            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // 바깥 경계와 안쪽 경계를 각각 1.5px 깎아 계단을 없앤다.
                float alpha = Mathf.Min(
                    Mathf.Clamp01((outer - distance) / feather),
                    Mathf.Clamp01((distance - inner) / feather));

                pixels[y * size + x] =
                    new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        generatedRingTexture.SetPixels32(pixels);
        generatedRingTexture.Apply(false, false);

        generatedRingSprite = Sprite.Create(
            generatedRingTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);

        generatedRingSprite.name = "OpeningSkipRing";
        generatedRingSprite.hideFlags = HideFlags.DontSave;

        return generatedRingSprite;
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
