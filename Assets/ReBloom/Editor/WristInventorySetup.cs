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
/// └── WristInventoryCanvas    ← 신규. Canvas(WorldSpace) + CanvasGroup + WristInventoryView
///     └── Icon                ← 신규. Image (Raycast Target Off)
/// </code>
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

        // ─── 6. WristInventoryView ───
        WristInventoryView view = canvasGo.GetComponent<WristInventoryView>();
        if (view == null)
        {
            view = canvasGo.AddComponent<WristInventoryView>();
            Debug.Log("  + WristInventoryView");
            changed = true;
        }

        // ─── 7. 참조 배선 ───
        if (WireReferences(view, iconImage, group))
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
            "    WristInventoryView   iconImage / canvasGroup 자동 연결\n" +
            $"    GraphicRaycaster     추가하지 않음\n" +
            $"  └ {IconName}\n" +
            "      RectTransform  부모 꽉 채움 (anchor 0~1, offset 0)\n" +
            "      Image          sprite 비움, Raycast Target Off, Preserve Aspect On");
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
