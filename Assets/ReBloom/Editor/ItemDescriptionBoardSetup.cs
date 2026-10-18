using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 상자 설명 보드의 공통 프리팹을 만드는 1회용 셋업 도구.
///
/// 만들어지는 프리팹 —
/// <code>
/// ItemDescriptionBoard            Canvas(WorldSpace, SO 1) + CanvasGroup(0) + ItemDescriptionBoard
/// ├── Panel                       어두운 반투명 배경
/// ├── AccentBar                   테두리/구분선 컨테이너 (자체 Image 없음)
/// │   ├── EdgeTop / EdgeBottom     상하 2px 강조선
/// │   ├── EdgeLeft / EdgeRight     좌우 2px 강조선
/// │   └── Divider                  아이콘과 글 사이 3px 세로선
/// ├── Icon                        아이템 아이콘 (Preserve Aspect)
/// ├── Title                        56pt
/// └── Description                  34pt, 2줄
/// </code>
///
/// 왜 에디터 도구인가:
/// <see cref="TextMeshProUGUI"/> 의 직렬화 블록은 한 컴포넌트가 80줄이 넘고 패키지
/// 버전에 따라 필드가 달라진다. 프리팹 YAML을 손으로 쓰면 글자 하나만 어긋나도
/// 컴포넌트가 깨져 Inspector에서 복구가 어렵다. Unity의 프리팹 API로 만들면
/// 현재 설치된 TMP 버전이 쓰는 직렬화 형식이 그대로 나온다.
/// (프로젝트의 Stage1ItemSetup / StartSceneMenuSetup 과 같은 패턴이다)
///
/// 안전 규칙 —
///  · GraphicRaycaster를 붙이지 않는다. Collider도 만들지 않는다.
///  · 모든 Graphic의 raycastTarget을 끈다.
///  · CanvasGroup은 alpha 0 / interactable false / blocksRaycasts false.
///  · 프리팹이 <b>이미 있으면 덮어쓰지 않는다</b>. 실기에서 맞춘 여백과 폰트 크기를
///    날리지 않기 위해서다. 다시 만들려면 기존 프리팹을 지우고 실행한다.
///  · 씬·XR Rig·아이템 프리팹·획득 스크립트는 건드리지 않는다. 이 도구는 새 에셋
///    하나만 쓴다.
/// </summary>
public static class ItemDescriptionBoardSetup
{
    private const string PrefabFolder = "Assets/ReBloom/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/ItemDescriptionBoard.prefab";
    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    // ── 레이아웃 (캔버스 단위) ─────────────────────────────────────────────
    private static readonly Vector2 BoardSize = new Vector2(700f, 360f);
    private const float BoardScale = 0.0012f;          // 월드 0.84 × 0.43 m

    private const float EdgeThickness = 2f;            // 테두리 두께
    private const float DividerWidth = 3f;             // 세로 구분선 두께
    private const float DividerHeight = 240f;
    private const float DividerX = 252f;               // 보드 왼쪽 끝 기준

    private const float IconSize = 180f;
    private const float IconLeft = 46f;

    private const float TextLeft = 286f;               // 구분선 오른쪽
    private const float TextRight = 36f;               // 오른쪽 여백
    private const float TitleTop = 40f;
    private const float TitleHeight = 76f;
    private const float DescriptionTop = 128f;
    private const float DescriptionBottom = 40f;

    private const float TitleFontSize = 56f;
    private const float DescriptionFontSize = 34f;

    // ── 색 ────────────────────────────────────────────────────────────────
    // 강조색 기본값은 Lobby의 EarBtn(#3DB3FF)과 같다. 인스턴스에서 바꾼다.
    private static readonly Color AccentDefault = new Color(0.24056602f, 0.7022988f, 1f, 1f);
    private static readonly Color PanelColor = new Color(0.039f, 0.071f, 0.090f, 0.78f);
    private static readonly Color TitleColor = new Color(0.95f, 0.98f, 1f, 1f);
    private static readonly Color DescriptionColor = new Color(0.80f, 0.87f, 0.92f, 1f);

    private const float EdgeAlpha = 0.85f;
    private const float DividerAlpha = 0.45f;

    private const string PlaceholderTitle = "아이템 이름";
    private const string PlaceholderDescription =
        "여기에 효과 설명을 입력합니다.\n두 번째 줄도 들어갑니다.";

    [MenuItem("Re_Bloom/Items/Item Description Board Prefab - Dry Run", priority = 320)]
    private static void DryRun() => Create(false);

    [MenuItem("Re_Bloom/Items/Item Description Board Prefab - Create", priority = 321)]
    private static void CreateMenu() => Create(true);

    [MenuItem("Re_Bloom/Items/Item Description Board Prefab - Validate", priority = 322)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 생성 ─────────────────────────────

    private static void Create(bool apply)
    {
        string mode = apply ? "CREATE" : "DRY RUN";
        Debug.Log($"===== 아이템 설명 보드 프리팹 [{mode}] =====");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.LogWarning(
                $"이미 존재합니다: {PrefabPath}\n" +
                "실기에서 맞춘 값을 덮어쓰지 않기 위해 다시 만들지 않습니다. " +
                "처음부터 다시 만들려면 기존 프리팹을 삭제하고 실행하세요. " +
                "구조만 점검하려면 'Item Description Board Prefab - Validate' 를 쓰세요.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            Debug.LogError($"폴더가 없습니다: {PrefabFolder}");
            return;
        }

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);

        if (font == null)
        {
            Debug.LogWarning(
                $"한글 TMP 폰트를 찾지 못했습니다: {KoreanFontPath}\n" +
                "TMP 기본 폰트에는 한글 글리프가 없어 문구가 깨집니다. " +
                "생성 후 Title / Description 의 Font Asset을 직접 지정하세요.");
        }

        ReportPlan(font != null);

        if (!apply)
        {
            Debug.Log("DRY RUN이므로 에셋을 만들지 않았습니다. 만들려면 Create를 실행하세요.");
            return;
        }

        GameObject root = BuildHierarchy(font, out List<Graphic> accents);

        try
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);

            if (!success || saved == null)
            {
                Debug.LogError($"프리팹 저장에 실패했습니다: {PrefabPath}");
                return;
            }

            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"프리팹을 만들었습니다: {PrefabPath}  (강조 Graphic {accents.Count}개 연결)");

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        Validate();
    }

    /// <summary>
    /// 프리팹으로 저장할 임시 계층을 만든다. 씬에는 올리지 않는다.
    /// </summary>
    private static GameObject BuildHierarchy(TMP_FontAsset font, out List<Graphic> accents)
    {
        accents = new List<Graphic>();

        // ── 루트 ────────────────────────────────────────────────
        var root = new GameObject("ItemDescriptionBoard", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = BoardSize;
        rootRect.localPosition = Vector3.zero;
        rootRect.localRotation = Quaternion.identity;
        rootRect.localScale = Vector3.one * BoardScale;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1;   // PickupPrompt(0)보다 앞에 그려 겹칠 때 순서를 확정한다
        // GraphicRaycaster / CanvasScaler 를 일부러 넣지 않는다.

        var group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        // ── Panel ───────────────────────────────────────────────
        RectTransform panel = CreateChild("Panel", rootRect);
        Stretch(panel, 0f, 0f, 0f, 0f);
        Image panelImage = AddImage(panel, PanelColor);

        // ── AccentBar (컨테이너) ────────────────────────────────
        RectTransform accentBar = CreateChild("AccentBar", rootRect);
        Stretch(accentBar, 0f, 0f, 0f, 0f);
        // 자체 Image 없음. 테두리 조각들만 담는다.

        accents.Add(CreateEdge("EdgeTop", accentBar, EdgeSide.Top));
        accents.Add(CreateEdge("EdgeBottom", accentBar, EdgeSide.Bottom));
        accents.Add(CreateEdge("EdgeLeft", accentBar, EdgeSide.Left));
        accents.Add(CreateEdge("EdgeRight", accentBar, EdgeSide.Right));

        RectTransform divider = CreateChild("Divider", accentBar);
        divider.anchorMin = new Vector2(0f, 0.5f);
        divider.anchorMax = new Vector2(0f, 0.5f);
        divider.pivot = new Vector2(0.5f, 0.5f);
        divider.sizeDelta = new Vector2(DividerWidth, DividerHeight);
        divider.anchoredPosition = new Vector2(DividerX, 0f);
        accents.Add(AddImage(divider, WithAlpha(AccentDefault, DividerAlpha)));

        // ── Icon ────────────────────────────────────────────────
        RectTransform icon = CreateChild("Icon", rootRect);
        icon.anchorMin = new Vector2(0f, 0.5f);
        icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0f, 0.5f);
        icon.sizeDelta = new Vector2(IconSize, IconSize);
        icon.anchoredPosition = new Vector2(IconLeft, 0f);

        Image iconImage = AddImage(icon, Color.white);
        iconImage.preserveAspect = true;
        // sprite는 인스턴스별로 넣는다 (Echo_HearingAid_Icon / Lumen_Medicine_Icon).

        // ── Title ───────────────────────────────────────────────
        RectTransform title = CreateChild("Title", rootRect);
        title.anchorMin = new Vector2(0f, 1f);
        title.anchorMax = new Vector2(1f, 1f);
        title.pivot = new Vector2(0f, 1f);
        title.offsetMin = new Vector2(TextLeft, -(TitleTop + TitleHeight));
        title.offsetMax = new Vector2(-TextRight, -TitleTop);

        TextMeshProUGUI titleText = AddText(
            title, font, PlaceholderTitle, TitleFontSize, TitleColor,
            TextAlignmentOptions.TopLeft);

        // ── Description ─────────────────────────────────────────
        RectTransform description = CreateChild("Description", rootRect);
        Stretch(description, TextLeft, TextRight, DescriptionTop, DescriptionBottom);

        TextMeshProUGUI descriptionText = AddText(
            description, font, PlaceholderDescription, DescriptionFontSize, DescriptionColor,
            TextAlignmentOptions.TopLeft);
        descriptionText.lineSpacing = 8f;

        // ── 스크립트 + 참조 배선 ────────────────────────────────
        var board = root.AddComponent<ItemDescriptionBoard>();

        var so = new SerializedObject(board);
        SetObject(so, "canvasGroup", group);
        SetObject(so, "iconImage", iconImage);
        SetObject(so, "titleText", titleText);
        SetObject(so, "descriptionText", descriptionText);
        SetColor(so, "accentColor", AccentDefault);
        SetArray(so, "accentGraphics", accents);
        so.ApplyModifiedPropertiesWithoutUndo();

        // anchor는 비워 둔다. 씬 배치 단계에서 ItemBox_low를 연결한다.
        // (씬 오브젝트는 프리팹 에셋에서 참조할 수 없다)

        // panelImage는 강조색 대상이 아니므로 변수만 쓰고 끝낸다.
        panelImage.raycastTarget = false;

        return root;
    }

    private enum EdgeSide { Top, Bottom, Left, Right }

    private static Image CreateEdge(string name, RectTransform parent, EdgeSide side)
    {
        RectTransform rect = CreateChild(name, parent);

        switch (side)
        {
            case EdgeSide.Top:
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(0f, -EdgeThickness);
                rect.offsetMax = Vector2.zero;
                break;

            case EdgeSide.Bottom:
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = new Vector2(0f, EdgeThickness);
                break;

            case EdgeSide.Left:
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = new Vector2(EdgeThickness, 0f);
                break;

            default:
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.offsetMin = new Vector2(-EdgeThickness, 0f);
                rect.offsetMax = Vector2.zero;
                break;
        }

        return AddImage(rect, WithAlpha(AccentDefault, EdgeAlpha));
    }

    // ───────────────────────────── 검증 ─────────────────────────────

    private static void Validate()
    {
        Debug.Log("===== 아이템 설명 보드 프리팹 [VALIDATE] =====");

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (asset == null)
        {
            Debug.LogError($"프리팹이 없습니다: {PrefabPath}");
            return;
        }

        int problems = 0;

        // ── 구조 ────────────────────────────────────────────────
        string[] expected =
        {
            "Panel", "AccentBar", "AccentBar/EdgeTop", "AccentBar/EdgeBottom",
            "AccentBar/EdgeLeft", "AccentBar/EdgeRight", "AccentBar/Divider",
            "Icon", "Title", "Description",
        };

        foreach (string path in expected)
        {
            if (asset.transform.Find(path) == null)
            {
                Debug.LogError($"  ✗ 자식 없음: {path}", asset);
                problems++;
            }
        }

        // ── 루트 컴포넌트 ───────────────────────────────────────
        var canvas = asset.GetComponent<Canvas>();
        problems += Expect(canvas != null, "Canvas 있음", asset);

        if (canvas != null)
        {
            problems += Expect(canvas.renderMode == RenderMode.WorldSpace,
                $"Canvas.renderMode = WorldSpace (현재 {canvas.renderMode})", asset);
            problems += Expect(canvas.sortingOrder == 1,
                $"Canvas.sortingOrder = 1 (현재 {canvas.sortingOrder})", asset);
        }

        var group = asset.GetComponent<CanvasGroup>();
        problems += Expect(group != null, "CanvasGroup 있음", asset);

        if (group != null)
        {
            problems += Expect(Mathf.Approximately(group.alpha, 0f),
                $"CanvasGroup.alpha = 0 (현재 {group.alpha})", asset);
            problems += Expect(!group.interactable, "CanvasGroup.interactable = false", asset);
            problems += Expect(!group.blocksRaycasts, "CanvasGroup.blocksRaycasts = false", asset);
        }

        // ── XR 안전 ─────────────────────────────────────────────
        problems += Expect(asset.GetComponentInChildren<GraphicRaycaster>(true) == null,
            "GraphicRaycaster 없음", asset);
        problems += Expect(asset.GetComponentInChildren<Collider>(true) == null,
            "Collider 없음", asset);

        foreach (Graphic graphic in asset.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.raycastTarget)
            {
                Debug.LogError($"  ✗ raycastTarget이 켜져 있습니다: {graphic.name}", graphic);
                problems++;
            }
        }

        // ── 스크립트 참조 ───────────────────────────────────────
        var board = asset.GetComponent<ItemDescriptionBoard>();
        problems += Expect(board != null, "ItemDescriptionBoard 있음", asset);

        if (board != null)
        {
            var so = new SerializedObject(board);

            problems += ExpectRef(so, "canvasGroup");
            problems += ExpectRef(so, "iconImage");
            problems += ExpectRef(so, "titleText");
            problems += ExpectRef(so, "descriptionText");

            SerializedProperty accents = so.FindProperty("accentGraphics");
            problems += Expect(accents != null && accents.arraySize == 5,
                $"accentGraphics 5개 (현재 {(accents == null ? "필드 없음" : accents.arraySize.ToString())})",
                asset);

            SerializedProperty anchor = so.FindProperty("anchor");

            if (anchor != null && anchor.objectReferenceValue == null)
            {
                Debug.Log("  · anchor 비어 있음 — 씬 배치 단계에서 ItemBox_low를 연결하세요. (정상)");
            }
        }

        // ── 폰트 ────────────────────────────────────────────────
        foreach (TMP_Text text in asset.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == null)
            {
                Debug.LogError($"  ✗ {text.name}: Font Asset이 비어 있습니다.", text);
                problems++;
                continue;
            }

            string path = AssetDatabase.GetAssetPath(text.font);

            if (path != KoreanFontPath)
            {
                Debug.LogWarning(
                    $"  ! {text.name}: 폰트가 {path} 입니다. 한글이 깨질 수 있습니다 " +
                    $"(기대: {KoreanFontPath})", text);
            }
        }

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음."
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    private static int Expect(bool condition, string label, Object context)
    {
        if (condition)
        {
            Debug.Log($"  ✓ {label}", context);
            return 0;
        }

        Debug.LogError($"  ✗ {label}", context);
        return 1;
    }

    private static int ExpectRef(SerializedObject so, string fieldName)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ '{fieldName}' 필드가 없습니다. 스크립트가 바뀌었는지 확인하세요.");
            return 1;
        }

        if (property.objectReferenceValue == null)
        {
            Debug.LogError($"  ✗ {fieldName} 참조가 비어 있습니다.");
            return 1;
        }

        Debug.Log($"  ✓ {fieldName} → {property.objectReferenceValue.name}");
        return 0;
    }

    // ───────────────────────────── 계획 보고 ─────────────────────────────

    private static void ReportPlan(bool hasFont)
    {
        Debug.Log(
            $"  대상 경로: {PrefabPath}\n" +
            $"  보드 크기: {BoardSize.x} × {BoardSize.y} @ scale {BoardScale} " +
            $"→ 월드 {BoardSize.x * BoardScale:F2} × {BoardSize.y * BoardScale:F2} m\n" +
            "  ItemDescriptionBoard\n" +
            "    Canvas         Render Mode = World Space, Sorting Order = 1\n" +
            "    CanvasGroup    alpha 0, interactable false, blocksRaycasts false\n" +
            "    ItemDescriptionBoard   showDistance 3 / hideHysteresis 0.25 / fadeDuration 0.3\n" +
            "    GraphicRaycaster       추가하지 않음\n" +
            "  ├ Panel          어두운 반투명 (#0A1217, a 0.78)\n" +
            "  ├ AccentBar      컨테이너\n" +
            $"  │ ├ EdgeTop / EdgeBottom / EdgeLeft / EdgeRight   {EdgeThickness}px 강조선\n" +
            $"  │ └ Divider      {DividerWidth}px × {DividerHeight} 세로 구분선 (x {DividerX})\n" +
            $"  ├ Icon           {IconSize} × {IconSize}, Preserve Aspect, sprite 비움\n" +
            $"  ├ Title          {TitleFontSize}pt, TopLeft\n" +
            $"  └ Description    {DescriptionFontSize}pt, TopLeft, 2줄\n" +
            $"  한글 폰트: {(hasFont ? "찾음" : "없음 — 생성 후 직접 지정 필요")}");
    }

    // ───────────────────────────── 보조 ─────────────────────────────

    private static RectTransform CreateChild(string name, RectTransform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        child.layer = parent.gameObject.layer;

        var rect = (RectTransform)child.transform;
        rect.localPosition = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        return rect;
    }

    /// <summary>네 변을 부모에 붙인다. 값은 각 변의 안쪽 여백(px).</summary>
    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static Image AddImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;   // XR Ray/Poke가 이 UI를 집지 않게 한다
        return image;
    }

    private static TextMeshProUGUI AddText(
        RectTransform rect, TMP_FontAsset font, string content,
        float fontSize, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.alignment = alignment;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;

        if (font != null)
            text.font = font;

        return text;
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        return new Color(color.r, color.g, color.b, alpha);
    }

    private static void SetObject(SerializedObject so, string fieldName, Object value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! ItemDescriptionBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static void SetColor(SerializedObject so, string fieldName, Color value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! ItemDescriptionBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.colorValue = value;
    }

    private static void SetArray(SerializedObject so, string fieldName, List<Graphic> values)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! ItemDescriptionBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
