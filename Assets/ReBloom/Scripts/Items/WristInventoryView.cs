using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 왼쪽 손목 인벤토리. 로컬 플레이어가 보유한 아이템(보청기 / 약통)을 작은 아이콘으로 보여준다.
///
/// 값의 출처는 <see cref="PlayerItemInventory.LocalInstance"/> 하나뿐이고, 이 컴포넌트는
/// <b>읽기만</b> 한다. 네트워크 로직도, 입력 처리도 없다.
///
/// 배치: XR Origin (XR Rig)/Camera Offset/Left Controller 하위.
/// XR Rig는 각 기기에 하나뿐이고 네트워크 오브젝트가 아니므로, 여기 붙인 UI는
/// 구조적으로 이미 로컬 전용이다. 오른손목 <see cref="ResonanceGaugeView"/>와 같은 방식이다.
/// Left Controller에는 TrackedPoseDriver가 있어 손 추종도 공짜로 따라온다.
///
/// 스테이지 전환을 버티는 방법:
/// XR Rig는 PersistentVRSystem 아래에서 DontDestroyOnLoad로 살아남지만, 아바타와
/// <see cref="PlayerItemInventory"/>는 스테이지마다 재스폰된다. 그래서
/// <see cref="PlayerItemInventory.LocalInstance"/>는 전환 중 잠시 null이 되고,
/// 재스폰 직후에는 [Networked] 값이 아직 도착하지 않아 한두 프레임 None으로 보이기도 한다.
/// 이 둘을 그대로 믿으면 아이콘이 꺼졌다 켜지며 깜빡인다.
///
/// 그래서 "마지막으로 확인한 상태"(<c>displayedKind</c>)를 들고 있고,
///  - LocalInstance가 null이면 아무것도 바꾸지 않는다(그대로 유지).
///  - 보유로 보이면 즉시 반영한다(획득은 늦게 보여줄 이유가 없다).
///  - 미보유로 보이면 <c>clearGraceSeconds</c>만큼 연속으로 미보유일 때만 지운다.
/// 최초 진입 시 <c>displayedKind</c>는 None이므로 "미보유로 시작"은 자동으로 성립한다.
/// </summary>
[AddComponentMenu("ReBloom/Wrist Inventory View")]
public class WristInventoryView : MonoBehaviour
{
    [Header("아이콘")]
    [Tooltip("아이콘을 그릴 Image. 비우면 자식에서 찾는다.")]
    [SerializeField] private Image iconImage;

    [Tooltip("보청기(에코) 아이콘. 비우면 임시 아이콘을 코드로 만들어 쓴다.")]
    [SerializeField] private Sprite hearingAidIcon;

    [Tooltip("약통(루멘) 아이콘. 비우면 임시 아이콘을 코드로 만들어 쓴다.")]
    [SerializeField] private Sprite medicineIcon;

    [Tooltip("아이콘에 곱해지는 색. 전용 스프라이트로 교체하면 보통 흰색으로 둔다.")]
    [SerializeField] private Color iconTint = Color.white;

    [Header("효과 타이머")]
    [Tooltip("아이콘 위에 덮이는 진행률 오버레이. Image Type = Filled / Fill Method = Vertical / Origin = Top. " +
             "비우면 자식 'Overlay'에서 찾는다.")]
    [SerializeField] private Image overlayImage;

    [Tooltip("아이콘 중앙의 남은 시간 숫자. 비우면 자식 'TimerText'에서 찾는다.")]
    [SerializeField] private TMP_Text timerText;

    [Tooltip("오버레이 색. 반투명 검정을 쓴다.")]
    [SerializeField] private Color overlayColor = new Color(0f, 0f, 0f, 0.65f);

    [Tooltip("남은 시간 숫자 색.")]
    [SerializeField] private Color timerTextColor = Color.white;

    [Header("표시")]
    [Tooltip("보이기/숨기기에 쓰는 CanvasGroup. 비우면 이 오브젝트에서 찾는다.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("나타나고 사라지는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.25f;

    [Tooltip("완전히 숨겨졌을 때 Canvas 렌더링까지 끌지. 보이지 않는 쿼드를 그리지 않게 한다.")]
    [SerializeField] private bool disableCanvasWhenHidden = true;

    [Header("깜빡임 방지")]
    [Tooltip("미보유로 보이는 상태가 이만큼(초) 이어져야 아이콘을 지운다. 스테이지 재스폰 직후 [Networked] 값이 도착하기 전의 공백을 흡수한다.")]
    [SerializeField, Min(0f)] private float clearGraceSeconds = 1f;

    [Header("디버그")]
    [Tooltip("표시 아이템이 바뀔 때 로그를 남긴다.")]
    [SerializeField] private bool logStateChange;

    // 화면에 실제로 띄우고 있는 아이템. LocalInstance가 없어도 유지된다.
    private ItemKind displayedKind = ItemKind.None;

    // 아이콘이 이미 반영된 아이템. 매 프레임 sprite를 다시 대입하지 않기 위한 캐시.
    private ItemKind appliedKind = ItemKind.None;
    private bool iconApplied;

    // 미보유로 보인 시간이 얼마나 이어졌는지.
    private float emptyStreak;

    // 어떤 인벤토리를 보고 있었는지. 재스폰으로 바뀌면 유예 시간을 다시 센다.
    private PlayerItemInventory trackedInventory;

    // ── 효과 타이머 표시 상태 ──────────────────────────────────────────────
    //
    // displayedKind와 같은 이유로 래치한다. 스테이지 전환 중에는 LocalInstance가 null이라
    // 네트워크 값을 읽을 수 없으므로, 마지막으로 본 값을 그대로 유지해야 UI가 깜빡이지 않는다.
    //
    // 로컬 타이머는 두지 않는다. 여기 담기는 값은 전부 [Networked] EffectRemainingSeconds를
    // 그대로 복사한 것이다. 컷씬·로딩으로 호스트가 깎지 않으면 이 값도 그대로 멈춘다.
    private bool displayedTimerVisible;
    private float displayedRemaining;
    private float displayedTotal = 30f;

    // 숫자를 매 프레임 다시 대입하지 않기 위한 캐시(TMP 메시 재생성을 줄인다).
    private int appliedTimerSeconds = -1;
    private bool timerApplied;

    private Canvas ownCanvas;
    private float alpha;

    // 임시 아이콘. 에셋이 아니라 코드로 만든 텍스처라 씬에 저장되지 않는다.
    private static Sprite placeholderHearingAid;
    private static Sprite placeholderMedicine;

    // 오버레이용 단색 스프라이트. Image.Type.Filled 는 sprite가 없으면 동작하지 않는다.
    private static Sprite overlayFillSprite;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        iconImage = GetComponentInChildren<Image>(true);
    }

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (iconImage == null)
            iconImage = GetComponentInChildren<Image>(true);

        ownCanvas = GetComponent<Canvas>();

        if (canvasGroup != null)
        {
            // 손목 UI는 읽기 전용이다. Poke / Near-Far / Teleport Interactor가 모두
            // Left Controller에 붙어 있으므로, 레이캐스트를 절대 받지 않게 못 박는다.
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (iconImage != null)
            iconImage.raycastTarget = false;

        ResolveTimerParts();

        // 최초 진입은 언제나 미보유 상태다.
        displayedKind = ItemKind.None;
        displayedTimerVisible = false;
        displayedRemaining = 0f;
        alpha = 0f;
        ApplyAlpha(0f);
        ApplyTimer(false);
    }

    /// <summary>
    /// 오버레이와 숫자 참조를 보강하고 XR 입력을 받지 않게 한다.
    /// 프리팹에서 이미 연결·설정돼 있지만, 비어 있어도 동작하도록 이름으로 한 번 더 찾는다.
    /// </summary>
    private void ResolveTimerParts()
    {
        if (overlayImage == null)
        {
            Transform child = transform.Find("Overlay");

            if (child != null)
                overlayImage = child.GetComponent<Image>();
        }

        if (timerText == null)
        {
            Transform child = transform.Find("TimerText");

            if (child != null)
                timerText = child.GetComponent<TMP_Text>();
        }

        if (overlayImage != null)
        {
            overlayImage.raycastTarget = false;
            overlayImage.color = overlayColor;
        }

        if (timerText != null)
        {
            timerText.raycastTarget = false;
            timerText.color = timerTextColor;
        }
    }

    private void Update()
    {
        UpdateDisplayedKind();
        ApplyIcon(displayedKind);
        ApplyTimer(displayedKind != ItemKind.None && displayedTimerVisible);
        UpdateFade(displayedKind != ItemKind.None);
    }

    /// <summary>
    /// 로컬 인벤토리를 다시 조회해 <c>displayedKind</c>를 갱신한다.
    /// 전환 중(LocalInstance == null)에는 손대지 않는다.
    /// </summary>
    private void UpdateDisplayedKind()
    {
        PlayerItemInventory inventory = PlayerItemInventory.LocalInstance;

        if (inventory == null)
        {
            // 아바타가 사라진 구간. 마지막 표시를 그대로 유지하고,
            // 다음 아바타가 올라오면 유예 시간을 처음부터 세도록 리셋한다.
            emptyStreak = 0f;
            trackedInventory = null;
            return;
        }

        if (inventory != trackedInventory)
        {
            trackedInventory = inventory;
            emptyStreak = 0f;
        }

        ItemKind observed = inventory.HeldItem;
        bool effectActive = inventory.IsEffectActive;

        // 효과가 돌고 있으면 손에 없어도 아이콘을 유지한다.
        // 사용하면 HeldItem은 즉시 None이 되지만, 남은 시간을 아이콘 위에 보여줘야 하므로
        // 획득 이력(AcquiredItem)으로 같은 아이콘을 계속 띄운다.
        if (observed == ItemKind.None && effectActive)
            observed = inventory.AcquiredItem;

        // 타이머 표시 값은 인벤토리를 볼 수 있을 때만 갱신한다.
        // 전환 중(LocalInstance == null)에는 위에서 이미 return 했으므로 마지막 값이 유지된다.
        displayedTimerVisible = effectActive;

        if (effectActive)
        {
            displayedRemaining = inventory.EffectRemainingSeconds;

            float total = inventory.EffectTotalSeconds;

            if (total > 0f)
                displayedTotal = total;
        }

        if (observed != ItemKind.None)
        {
            emptyStreak = 0f;
            SetDisplayedKind(observed);
            return;
        }

        // 여기까지 왔으면 손에도 없고 효과도 끝났다.
        //
        // 소비가 확정된 경우에는 유예 없이 즉시 지운다.
        // clearGraceSeconds는 "스테이지 재스폰 직후 [Networked] 값이 아직 안 온 공백"을
        // 흡수하기 위한 장치다. 소비는 그 공백과 성질이 다르다 — AcquiredItem이 남아 있는데
        // HeldItem과 남은 시간이 모두 비었다는 것은 효과까지 끝났다는 뜻이고, 되돌아올 값이 없다.
        // 한 번 지워진 뒤에는 다시 채워지지 않으므로 아이콘도 되살아나지 않는다.
        if (inventory.IsConsumed)
        {
            emptyStreak = 0f;
            displayedRemaining = 0f;
            SetDisplayedKind(ItemKind.None);
            return;
        }

        // 아직 아무것도 띄우지 않았다면 유예할 것이 없다.
        if (displayedKind == ItemKind.None)
            return;

        emptyStreak += Time.unscaledDeltaTime;
        if (emptyStreak >= clearGraceSeconds)
            SetDisplayedKind(ItemKind.None);
    }

    private void SetDisplayedKind(ItemKind kind)
    {
        if (displayedKind == kind)
            return;

        displayedKind = kind;

        if (logStateChange)
            Debug.Log($"[WristInventoryView] 표시 아이템 → {ItemKindRules.DisplayName(kind)}", this);
    }

    /// <summary>
    /// 아이템에 맞는 스프라이트를 Image에 반영한다. 바뀔 때만 대입한다.
    /// </summary>
    private void ApplyIcon(ItemKind kind)
    {
        if (iconImage == null)
            return;

        if (iconApplied && appliedKind == kind)
            return;

        appliedKind = kind;
        iconApplied = true;

        Sprite sprite = ResolveSprite(kind);
        iconImage.sprite = sprite;
        iconImage.color = iconTint;

        // 스프라이트가 없으면 Image가 흰 사각형을 그려버린다. 아예 끈다.
        iconImage.enabled = sprite != null;
    }

    /// <summary>
    /// 이 아이템의 스프라이트. Inspector에 연결된 것이 있으면 그것을, 없으면 임시 아이콘을 쓴다.
    /// </summary>
    private Sprite ResolveSprite(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.HearingAid:
                return hearingAidIcon != null ? hearingAidIcon : GetPlaceholderHearingAid();

            case ItemKind.Medicine:
                return medicineIcon != null ? medicineIcon : GetPlaceholderMedicine();

            default:
                return null;
        }
    }

    /// <summary>
    /// 진행률 오버레이와 남은 시간 숫자를 반영한다.
    ///
    /// 진행률은 <c>1 - 남은시간/총시간</c> 이다. 효과 시작 시 0이라 아이콘이 전부 보이고,
    /// 시간이 줄수록 1에 가까워져 어두운 영역이 넓어진다. Fill Origin이 Top이므로
    /// 어두운 영역은 <b>위에서 아래로</b> 차오른다.
    ///
    /// 여기서 쓰는 값은 전부 네트워크 타이머를 복사한 것이다. 별도의 로컬 카운트다운이 없으므로
    /// 컷씬·로딩으로 <c>EffectRemainingSeconds</c> 가 멈추면 숫자와 오버레이도 함께 멈춘다.
    /// </summary>
    private void ApplyTimer(bool show)
    {
        if (overlayImage != null)
        {
            if (overlayImage.enabled != show)
                overlayImage.enabled = show;

            if (show)
            {
                EnsureOverlayFill();

                float ratio = displayedTotal > 0f
                    ? displayedRemaining / displayedTotal
                    : 0f;

                overlayImage.fillAmount = Mathf.Clamp01(1f - ratio);
            }
        }

        if (timerText != null)
        {
            if (timerText.enabled != show)
                timerText.enabled = show;

            if (show)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(displayedRemaining));

                if (!timerApplied || seconds != appliedTimerSeconds)
                {
                    timerApplied = true;
                    appliedTimerSeconds = seconds;
                    timerText.text = seconds.ToString();
                }
            }
        }

        if (!show)
        {
            timerApplied = false;
            appliedTimerSeconds = -1;
        }
    }

    /// <summary>
    /// 오버레이가 Filled로 동작할 수 있게 보장한다.
    /// </summary>
    /// <remarks>
    /// <see cref="Image"/> 는 sprite가 null이면 type을 무시하고 단순 사각형을 그린다.
    /// 즉 <c>fillAmount</c> 가 전혀 반영되지 않는다. 전용 에셋을 만들지 않기 위해
    /// 단색 스프라이트를 코드로 한 번 만들어 쓴다. <c>SpriteMeshType.FullRect</c> 는
    /// Filled에 필수다(Tight 메시는 채움 계산이 어긋난다).
    /// 프리팹 쪽에서 이미 설정돼 있으면 이 메서드는 아무것도 바꾸지 않는다.
    /// </remarks>
    private void EnsureOverlayFill()
    {
        if (overlayImage.sprite == null)
        {
            if (overlayFillSprite == null)
                overlayFillSprite = CreateSolidSprite();

            overlayImage.sprite = overlayFillSprite;
        }

        if (overlayImage.type != Image.Type.Filled)
            overlayImage.type = Image.Type.Filled;

        if (overlayImage.fillMethod != Image.FillMethod.Vertical)
            overlayImage.fillMethod = Image.FillMethod.Vertical;

        // 위에서 아래로 차오르게 한다.
        if (overlayImage.fillOrigin != (int)Image.OriginVertical.Top)
            overlayImage.fillOrigin = (int)Image.OriginVertical.Top;
    }

    /// <summary>오버레이용 흰색 단색 스프라이트. 색은 Image.color가 결정한다.</summary>
    private static Sprite CreateSolidSprite()
    {
        const int size = 4;
        Texture2D texture = CreateTexture(size);
        Color[] pixels = new Color[size * size];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.white;

        texture.SetPixels(pixels);
        texture.Apply(false, false);

        return CreateSprite(texture);
    }

    private void UpdateFade(bool show)
    {
        float target = show ? 1f : 0f;

        if (!Mathf.Approximately(alpha, target))
        {
            float step = fadeDuration <= 0f
                ? 1f
                : Time.unscaledDeltaTime / fadeDuration;

            alpha = Mathf.MoveTowards(alpha, target, step);
        }

        ApplyAlpha(alpha);
    }

    private void ApplyAlpha(float value)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = value;

        if (disableCanvasWhenHidden && ownCanvas != null)
        {
            bool visible = value > 0.001f;
            if (ownCanvas.enabled != visible)
                ownCanvas.enabled = visible;
        }
    }

    // ───────────────────────── 임시 아이콘 ─────────────────────────
    //
    // 전용 스프라이트가 아직 없어도 동작을 확인할 수 있게, 두 아이템을 한눈에
    // 구분되는 도형으로 코드에서 만든다. Inspector에 전용 스프라이트를 연결하면
    // 이 코드는 더 이상 호출되지 않으므로, 나중에 지워도 된다.
    //
    // HideFlags.DontSave를 주어 씬/프리팹에 섞여 들어가지 않게 한다.

    /// <summary>보청기: 귀 모양을 떠올리게 하는 연녹색 링.</summary>
    private static Sprite GetPlaceholderHearingAid()
    {
        if (placeholderHearingAid != null)
            return placeholderHearingAid;

        const int size = 128;
        Color fill = new Color(0.44f, 0.90f, 0.72f, 1f);   // #6EE7B7 계열
        Texture2D texture = CreateTexture(size);
        Color[] pixels = new Color[size * size];

        float center = (size - 1) * 0.5f;
        float outer = size * 0.46f;
        float inner = size * 0.28f;
        float feather = 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // 링: 바깥 경계는 안으로, 안쪽 경계는 밖으로 페더링.
                float a = Mathf.Clamp01((outer - dist) / feather)
                        * Mathf.Clamp01((dist - inner) / feather);

                pixels[y * size + x] = new Color(fill.r, fill.g, fill.b, a);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);

        placeholderHearingAid = CreateSprite(texture);
        return placeholderHearingAid;
    }

    /// <summary>약통: 둥근 사각형 바탕 위의 흰 십자.</summary>
    private static Sprite GetPlaceholderMedicine()
    {
        if (placeholderMedicine != null)
            return placeholderMedicine;

        const int size = 128;
        Color body = new Color(0.95f, 0.45f, 0.45f, 1f);
        Color mark = Color.white;
        Texture2D texture = CreateTexture(size);
        Color[] pixels = new Color[size * size];

        float center = (size - 1) * 0.5f;
        float half = size * 0.40f;        // 둥근 사각형 반폭
        float radius = size * 0.14f;      // 모서리 반지름
        float barHalfThickness = size * 0.09f;
        float barHalfLength = size * 0.25f;
        float feather = 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Abs(x - center);
                float dy = Mathf.Abs(y - center);

                // 둥근 사각형: 모서리 반지름을 뺀 사각형까지의 거리.
                float qx = Mathf.Max(dx - (half - radius), 0f);
                float qy = Mathf.Max(dy - (half - radius), 0f);
                float cornerDist = Mathf.Sqrt(qx * qx + qy * qy);
                float bodyAlpha = Mathf.Clamp01((radius - cornerDist) / feather);
                if (dx > half || dy > half)
                    bodyAlpha = 0f;

                // 십자: 가로 막대 또는 세로 막대 안쪽.
                bool inCross = (dx <= barHalfLength && dy <= barHalfThickness)
                            || (dy <= barHalfLength && dx <= barHalfThickness);

                Color color = inCross ? mark : body;
                pixels[y * size + x] = new Color(color.r, color.g, color.b, bodyAlpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);

        placeholderMedicine = CreateSprite(texture);
        return placeholderMedicine;
    }

    private static Texture2D CreateTexture(int size)
    {
        return new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "WristInventoryPlaceholder",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
    }

    private static Sprite CreateSprite(Texture2D texture)
    {
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);

        sprite.name = "WristInventoryPlaceholder";
        sprite.hideFlags = HideFlags.DontSave;
        return sprite;
    }
}
