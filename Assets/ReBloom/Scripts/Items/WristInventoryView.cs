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

    private Canvas ownCanvas;
    private float alpha;

    // 임시 아이콘. 에셋이 아니라 코드로 만든 텍스처라 씬에 저장되지 않는다.
    private static Sprite placeholderHearingAid;
    private static Sprite placeholderMedicine;

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

        // 최초 진입은 언제나 미보유 상태다.
        displayedKind = ItemKind.None;
        alpha = 0f;
        ApplyAlpha(0f);
    }

    private void Update()
    {
        UpdateDisplayedKind();
        ApplyIcon(displayedKind);
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

        if (observed != ItemKind.None)
        {
            emptyStreak = 0f;
            SetDisplayedKind(observed);
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
