using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// XR Rig 프리팹의 Left Controller 아래에 왼쪽 손목 인벤토리 UI를 붙이는 1회용 셋업 도구.
///
/// 만들어지는 계층 —
/// <code>
/// Left Controller
/// ├── ... (Poke / Near-Far / Teleport Interactor, hand_l, LeftHandSendTarget 등 기존 자식)
/// ├── TutorialCanvas          ← 기존. 건드리지 않는다
/// └── WristInventoryCanvas    ← Canvas(WorldSpace) + CanvasGroup + WristInventoryView
///     ├── Icon                ← Image. 보청기/약통 스프라이트 (Raycast Target Off)
///     ├── Overlay             ← Image Filled/Vertical/Top. 효과 남은 시간 진행률
///     └── TimerText           ← TMP. 아이콘 중앙의 남은 초 숫자
/// </code>
/// 드로우 순서가 하이어라키 순서이므로 Icon → Overlay → TimerText 로 둔다.
/// 그래야 어두운 영역이 아이콘을 덮고, 숫자가 그 위에 보인다.
///
/// 왜 에디터 도구인가:
/// 이 프리팹은 12,000줄이 넘고 Left Controller 아래에만 중첩 프리팹 인스턴스가 6개
/// (Poke / Near-Far / Teleport Interactor, Left Controller Visual, hand_l ×2) 있다.
/// YAML에 손으로 쓰려면 fileID를 새로 발급하고 m_Children 배열을 정확한 위치에 끼워
/// 넣어야 하는데, 한 글자만 틀려도 리그 전체가 깨진다. Unity의 프리팹 API를 쓰면
/// 기존 오브젝트의 fileID와 중첩 프리팹 연결이 그대로 보존된다.
///
/// 안전 규칙 —
///  - WristInventoryCanvas 서브트리 <b>밖</b>은 아무것도 읽기만 한다.
///  - 이미 있는 오브젝트의 <b>Transform 값은 덮어쓰지 않는다</b>. 초기값은 "처음 만들 때"만
///    넣고, 그 뒤에는 Inspector에서 손으로 맞춘 위치를 존중한다.
///  - GraphicRaycaster는 추가하지 않는다. Left Controller에는 Poke / Near-Far /
///    Teleport Interactor가 모두 붙어 있어, 손목 UI가 레이캐스트를 받으면 상호작용
///    판정을 가로챌 수 있다.
///
/// Dry Run으로 무엇이 바뀔지 먼저 확인하고, Apply로 실제 저장한다.
/// </summary>
public static class WristInventorySetup
{
    private const string PrefabPath = "Assets/ReBloom/Prefabs/Characters/XR Origin (XR Rig).prefab";

    private const string CameraOffsetName = "Camera Offset";
    private const string LeftControllerName = "Left Controller";
    private const string CanvasName = "WristInventoryCanvas";
    private const string IconName = "Icon";
    private const string OverlayName = "Overlay";
    private const string TimerTextName = "TimerText";

    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    /// <summary>진행률 오버레이 색. 반투명 검정.</summary>
    private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.65f);

    /// <summary>남은 시간 숫자 크기. 캔버스가 120×120 단위라 두 자리 숫자가 꽉 차게 들어간다.</summary>
    private const float TimerFontSize = 64f;

    // 초기 배치값. 처음 생성할 때만 적용된다.
    private static readonly Vector3 InitialLocalPosition = new Vector3(0f, 0.02f, -0.07f);
    private static readonly Vector3 InitialLocalEuler = new Vector3(60f, 0f, 0f);
    private static readonly Vector2 InitialSizeDelta = new Vector2(120f, 120f);
    private static readonly Vector3 InitialLocalScale = new Vector3(0.0004f, 0.0004f, 0.0004f);

    [MenuItem("Re_Bloom/Items/Wrist Inventory Setup - Dry Run", priority = 310)]
    private static void DryRun() => Run(false);

    [MenuItem("Re_Bloom/Items/Wrist Inventory Setup - Apply", priority = 311)]
    private static void Apply() => Run(true);

    private static void Run(bool apply)
    {
        string mode = apply ? "APPLY" : "DRY RUN";
        Debug.Log($"===== 왼손목 인벤토리 셋업 [{mode}] =====");

        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogError($"프리팹을 열 수 없습니다: {PrefabPath}");
            return;
        }

        bool changed = false;

        try
        {
            Transform leftController = FindLeftController(prefabRoot.transform);
            if (leftController == null)
            {
                Debug.LogError(
                    $"'{CameraOffsetName}/{LeftControllerName}' 를 찾을 수 없습니다. " +
                    "프리팹 구조가 바뀌었는지 확인해주세요.");
                return;
            }

            Debug.Log($"대상 부모: {Path(leftController)}  (기존 자식 {leftController.childCount}개)");

            changed = SetupCanvas(leftController, apply);
        }
        finally
        {
            if (apply && changed)
            {
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                Debug.Log($"프리팹을 저장했습니다: {PrefabPath}");
            }
            else if (apply)
            {
                Debug.Log("변경할 내용이 없어 저장하지 않았습니다.");
            }

            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

        if (!apply)
            Debug.Log("DRY RUN이므로 아무것도 저장하지 않았습니다. 적용하려면 Apply를 실행하세요.");
    }

    /// <summary>Camera Offset 아래의 Left Controller. 이름이 같은 다른 오브젝트에 속지 않도록 경로로 찾는다.</summary>
    private static Transform FindLeftController(Transform root)
    {
        Transform cameraOffset = root.Find(CameraOffsetName);
        if (cameraOffset == null)
            return null;

        return cameraOffset.Find(LeftControllerName);
    }

    private static bool SetupCanvas(Transform leftController, bool apply)
    {
        bool changed = false;

        // ─── 1. WristInventoryCanvas ───
        Transform existing = leftController.Find(CanvasName);
        bool created = existing == null;
        GameObject canvasGo;

        if (created)
        {
            Debug.Log($"+ '{CanvasName}' 생성 (Left Controller 직속 자식)");
            if (!apply)
            {
                ReportPlannedCreation();
                return true;
            }

            canvasGo = new GameObject(CanvasName, typeof(RectTransform));
            canvasGo.transform.SetParent(leftController, false);
            canvasGo.layer = leftController.gameObject.layer;
            changed = true;
        }
        else
        {
            canvasGo = existing.gameObject;
            Debug.Log($"= '{CanvasName}' 이미 존재합니다. 누락된 컴포넌트와 참조만 보완합니다.");
        }

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();

        // ─── 2. RectTransform 초기값 (생성할 때만) ───
        if (created)
        {
            canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
            canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
            canvasRect.pivot = new Vector2(0.5f, 0.5f);
            canvasRect.sizeDelta = InitialSizeDelta;
            canvasRect.localPosition = InitialLocalPosition;
            canvasRect.localRotation = Quaternion.Euler(InitialLocalEuler);
            canvasRect.localScale = InitialLocalScale;

            Debug.Log($"  초기 배치: pos={InitialLocalPosition} euler={InitialLocalEuler} " +
                      $"size={InitialSizeDelta} scale={InitialLocalScale}");
        }
        else
        {
            Debug.Log($"  배치 유지: pos={canvasRect.localPosition} " +
                      $"euler={canvasRect.localEulerAngles} size={canvasRect.sizeDelta} " +
                      $"scale={canvasRect.localScale}  (Inspector 조절값을 덮어쓰지 않습니다)");
        }

        // ─── 3. Canvas (World Space) ───
        Canvas canvas = canvasGo.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 0;
            Debug.Log("  + Canvas (Render Mode = World Space)");
            changed = true;
        }
        else if (canvas.renderMode != RenderMode.WorldSpace)
        {
            Debug.Log($"  ~ Canvas.renderMode: {canvas.renderMode} → WorldSpace");
            canvas.renderMode = RenderMode.WorldSpace;
            changed = true;
        }

        // GraphicRaycaster는 의도적으로 추가하지 않는다. 혹시 손으로 붙였다면 알려준다.
        if (canvasGo.GetComponent<GraphicRaycaster>() != null)
        {
            Debug.LogWarning(
                $"  ! '{CanvasName}' 에 GraphicRaycaster가 붙어 있습니다. " +
                "Left Controller의 Poke / Near-Far / Teleport Interactor 판정을 가로챌 수 있으니 " +
                "직접 제거를 권합니다.");
        }

        // ─── 4. CanvasGroup ───
        CanvasGroup group = canvasGo.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = canvasGo.AddComponent<CanvasGroup>();
            Debug.Log("  + CanvasGroup");
            changed = true;
        }

        // 읽기 전용 UI. 레이캐스트를 받지 않도록 프리팹 단계에서도 못 박아 둔다.
        if (group.interactable || group.blocksRaycasts)
        {
            Debug.Log($"  ~ CanvasGroup interactable/blocksRaycasts: " +
                      $"{group.interactable}/{group.blocksRaycasts} → false/false");
            group.interactable = false;
            group.blocksRaycasts = false;
            changed = true;
        }

        // ─── 5. Icon ───
        Transform iconTransform = canvasRect.Find(IconName);
        Image iconImage;

        if (iconTransform == null)
        {
            GameObject iconGo = new GameObject(IconName, typeof(RectTransform));
            iconGo.transform.SetParent(canvasRect, false);
            iconGo.layer = canvasGo.layer;

            RectTransform iconRect = iconGo.GetComponent<RectTransform>();
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            iconRect.localScale = Vector3.one;
            iconRect.localRotation = Quaternion.identity;

            iconImage = iconGo.AddComponent<Image>();
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.color = Color.white;

            Debug.Log("  + Icon (Image, 부모 꽉 채움, Raycast Target Off)");
            changed = true;
        }
        else
        {
            iconImage = iconTransform.GetComponent<Image>();
            if (iconImage == null)
            {
                iconImage = iconTransform.gameObject.AddComponent<Image>();
                iconImage.preserveAspect = true;
                Debug.Log("  + Icon에 Image 추가");
                changed = true;
            }

            if (iconImage.raycastTarget)
            {
                iconImage.raycastTarget = false;
                Debug.Log("  ~ Icon.raycastTarget: true → false");
                changed = true;
            }

            Debug.Log("  = Icon 이미 존재합니다. 배치는 유지합니다.");
        }

        // ─── 6. Overlay (효과 진행률) ───
        // Icon 뒤에 와야 아이콘 위에 덮인다. 같은 Canvas에서는 하이어라키 순서가 드로우 순서다.
        Transform overlayTransform = canvasRect.Find(OverlayName);
        Image overlayImage;

        if (overlayTransform == null)
        {
            GameObject overlayGo = new GameObject(OverlayName, typeof(RectTransform));
            overlayGo.transform.SetParent(canvasRect, false);
            overlayGo.layer = canvasGo.layer;

            StretchToParent(overlayGo.GetComponent<RectTransform>());

            overlayImage = overlayGo.AddComponent<Image>();
            overlayImage.raycastTarget = false;
            overlayImage.color = OverlayColor;
            overlayImage.type = Image.Type.Filled;
            overlayImage.fillMethod = Image.FillMethod.Vertical;
            overlayImage.fillOrigin = (int)Image.OriginVertical.Top;   // 위에서 아래로 차오름
            overlayImage.fillAmount = 0f;                              // 시작은 아이콘 전체가 보임
            overlayImage.enabled = false;                              // 효과 중에만 켠다

            Debug.Log("  + Overlay (Image Filled / Vertical / Origin Top, Raycast Target Off)");
            changed = true;
        }
        else
        {
            overlayImage = overlayTransform.GetComponent<Image>();

            if (overlayImage == null)
            {
                overlayImage = overlayTransform.gameObject.AddComponent<Image>();
                Debug.Log("  + Overlay에 Image 추가");
                changed = true;
            }

            // Filled 동작에 필요한 설정만 보정한다. 색과 배치는 손댄 값을 존중한다.
            changed |= EnsureFilled(overlayImage);

            Debug.Log("  = Overlay 이미 존재합니다.");
        }

        // ─── 7. TimerText (남은 시간 숫자) ───
        // Overlay 뒤에 와야 어두운 영역 위에 숫자가 보인다.
        Transform timerTransform = canvasRect.Find(TimerTextName);
        TMP_Text timerText;

        if (timerTransform == null)
        {
            GameObject timerGo = new GameObject(TimerTextName, typeof(RectTransform));
            timerGo.transform.SetParent(canvasRect, false);
            timerGo.layer = canvasGo.layer;

            StretchToParent(timerGo.GetComponent<RectTransform>());

            TextMeshProUGUI tmp = timerGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "30";
            tmp.fontSize = TimerFontSize;
            tmp.enableAutoSizing = false;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            tmp.enabled = false;   // 효과 중에만 켠다

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);

            if (font != null)
                tmp.font = font;
            else
                Debug.LogWarning($"  ! TMP 폰트를 찾지 못했습니다: {KoreanFontPath}. 숫자는 기본 폰트로도 보입니다.");

            timerText = tmp;

            Debug.Log($"  + TimerText (TextMeshProUGUI {TimerFontSize}pt, Center, Raycast Target Off)");
            changed = true;
        }
        else
        {
            timerText = timerTransform.GetComponent<TMP_Text>();

            if (timerText == null)
            {
                timerText = timerTransform.gameObject.AddComponent<TextMeshProUGUI>();
                Debug.Log("  + TimerText에 TextMeshProUGUI 추가");
                changed = true;
            }

            if (timerText.raycastTarget)
            {
                timerText.raycastTarget = false;
                Debug.Log("  ~ TimerText.raycastTarget: true → false");
                changed = true;
            }

            Debug.Log("  = TimerText 이미 존재합니다. 폰트 크기와 배치는 유지합니다.");
        }

        // ─── 8. 드로우 순서 보정 (Icon → Overlay → TimerText) ───
        if (EnsureSiblingOrder(canvasRect))
            changed = true;

        // ─── 9. WristInventoryView ───
        WristInventoryView view = canvasGo.GetComponent<WristInventoryView>();
        if (view == null)
        {
            view = canvasGo.AddComponent<WristInventoryView>();
            Debug.Log("  + WristInventoryView");
            changed = true;
        }

        // ─── 10. 참조 배선 ───
        if (WireReferences(view, iconImage, group))
            changed = true;

        if (SetObjectField(view, "overlayImage", overlayImage))
            changed = true;

        if (SetObjectField(view, "timerText", timerText))
            changed = true;

        Debug.Log(changed
            ? "셋업 완료. 아래 Hierarchy를 확인하세요."
            : "이미 모두 셋업되어 있습니다. 바꾼 것이 없습니다.");

        return changed;
    }

    /// <summary>
    /// View의 iconImage / canvasGroup 참조를 연결한다. 전용 스프라이트 필드는 비워 둔다
    /// (비어 있으면 View가 임시 아이콘을 코드로 만들어 쓴다).
    /// </summary>
    private static bool WireReferences(WristInventoryView view, Image icon, CanvasGroup group)
    {
        var so = new SerializedObject(view);
        bool changed = false;

        changed |= SetObjectField(so, "iconImage", icon);
        changed |= SetObjectField(so, "canvasGroup", group);

        if (changed)
            so.ApplyModifiedPropertiesWithoutUndo();

        SerializedProperty hearing = so.FindProperty("hearingAidIcon");
        SerializedProperty medicine = so.FindProperty("medicineIcon");
        bool hasHearing = hearing != null && hearing.objectReferenceValue != null;
        bool hasMedicine = medicine != null && medicine.objectReferenceValue != null;

        Debug.Log($"  스프라이트 연결 상태: 보청기={(hasHearing ? "연결됨" : "비어 있음 → 임시 아이콘")}, " +
                  $"약통={(hasMedicine ? "연결됨" : "비어 있음 → 임시 아이콘")}");

        return changed;
    }

    /// <summary>RectTransform을 부모에 꽉 채운다.</summary>
    private static void StretchToParent(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Image가 Filled로 동작할 수 있게 보정한다. 바꾼 것이 있으면 true.
    /// </summary>
    /// <remarks>
    /// 스프라이트는 <b>여기서 넣지 않는다</b>. Filled는 sprite가 있어야 동작하지만,
    /// 내장 UI 스프라이트는 SpriteMeshType이 보장되지 않아(Tight면 채움 계산이 어긋난다)
    /// 런타임의 <c>WristInventoryView.EnsureOverlayFill</c> 이 FullRect 단색 스프라이트를
    /// 만들어 넣는다. 프리팹에는 type/fillMethod/fillOrigin만 저장해 두면 된다.
    /// 오버레이는 프리팹에서 <c>enabled = false</c> 라 에디터 미리보기에도 보이지 않는다.
    /// </remarks>
    private static bool EnsureFilled(Image image)
    {
        bool changed = false;

        if (image.type != Image.Type.Filled)
        {
            image.type = Image.Type.Filled;
            Debug.Log($"  ~ {image.name}.type → Filled");
            changed = true;
        }

        if (image.fillMethod != Image.FillMethod.Vertical)
        {
            image.fillMethod = Image.FillMethod.Vertical;
            Debug.Log($"  ~ {image.name}.fillMethod → Vertical");
            changed = true;
        }

        if (image.fillOrigin != (int)Image.OriginVertical.Top)
        {
            image.fillOrigin = (int)Image.OriginVertical.Top;
            Debug.Log($"  ~ {image.name}.fillOrigin → Top (위에서 아래로 차오름)");
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Icon → Overlay → TimerText 순서를 보장한다. 같은 Canvas에서는 이 순서가 드로우 순서다.
    /// 바꾼 것이 있으면 true.
    /// </summary>
    private static bool EnsureSiblingOrder(RectTransform canvasRect)
    {
        string[] order = { IconName, OverlayName, TimerTextName };
        bool changed = false;

        for (int i = 0; i < order.Length; i++)
        {
            Transform child = canvasRect.Find(order[i]);

            if (child == null)
                continue;

            if (child.GetSiblingIndex() != i)
            {
                child.SetSiblingIndex(i);
                Debug.Log($"  ~ {order[i]} 드로우 순서 → {i}");
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>View의 private 직렬화 참조를 연결한다. 바꾼 것이 있으면 true.</summary>
    private static bool SetObjectField(WristInventoryView view, string fieldName, Object value)
    {
        var so = new SerializedObject(view);
        bool changed = SetObjectField(so, fieldName, value);

        if (changed)
            so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    private static bool SetObjectField(SerializedObject so, string fieldName, Object value)
    {
        SerializedProperty property = so.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogWarning($"  ! WristInventoryView에 '{fieldName}' 필드가 없습니다. 스크립트가 바뀌었는지 확인해주세요.");
            return false;
        }

        if (property.objectReferenceValue == value)
            return false;

        Debug.Log($"  ~ {fieldName}: {Describe(property.objectReferenceValue)} → {Describe(value)}");
        property.objectReferenceValue = value;
        return true;
    }

    private static string Describe(Object value) => value == null ? "(없음)" : value.name;

    private static void ReportPlannedCreation()
    {
        Debug.Log(
            "  [DRY RUN 계획]\n" +
            $"  {CanvasName}\n" +
            $"    RectTransform  pos={InitialLocalPosition} euler={InitialLocalEuler} " +
            $"size={InitialSizeDelta} scale={InitialLocalScale}\n" +
            "    Canvas         Render Mode = World Space, Sorting Order = 0\n" +
            "    CanvasGroup    alpha = 1 (런타임 Awake에서 0으로 시작), interactable/blocksRaycasts = false\n" +
            "    WristInventoryView   iconImage / canvasGroup / overlayImage / timerText 자동 연결\n" +
            $"    GraphicRaycaster     추가하지 않음\n" +
            $"  ├ {IconName}\n" +
            "      RectTransform  부모 꽉 채움 (anchor 0~1, offset 0)\n" +
            "      Image          sprite 비움, Raycast Target Off, Preserve Aspect On\n" +
            $"  ├ {OverlayName}       ← 효과 진행률\n" +
            "      RectTransform  부모 꽉 채움\n" +
            $"      Image          Filled / Vertical / Origin Top, color #000 a {OverlayColor.a:F2}, " +
            "fillAmount 0, enabled false (효과 중에만 켜짐)\n" +
            "                     sprite는 런타임에 FullRect 단색으로 채워짐\n" +
            $"  └ {TimerTextName}     ← 남은 시간 숫자\n" +
            "      RectTransform  부모 꽉 채움\n" +
            $"      TMP            {TimerFontSize}pt Center, 흰색, Raycast Target Off, enabled false\n" +
            "  드로우 순서         Icon → Overlay → TimerText");
    }

    private static string Path(Transform transform)
    {
        string path = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
