using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Stage1의 두 아이템 상자 위에 <see cref="ItemDescriptionBoard"/> 프리팹을 배치하고
/// 참조·문구·아이콘·강조색까지 전부 자동으로 채우는 셋업 도구.
///
/// 만들어지는 계층 —
/// <code>
/// Item_HearingAid
/// ├── HearingAid_low         (기존, 손대지 않음)
/// ├── ItemBox_low            (기존, 손대지 않음 — anchor로만 참조)
/// └── ItemDescriptionBoard   ← 신규 프리팹 인스턴스
///
/// Item_Medicine
/// ├── Medicine_low           (기존)
/// ├── ItemBox_low            (기존)
/// └── ItemDescriptionBoard   ← 신규 프리팹 인스턴스
/// </code>
///
/// 왜 <c>Item_*</c> 의 자식인가:
///  · 아이템 모델(<c>HearingAid_low</c>)의 자식으로 두면 <b>잡았을 때 손에 끌려간다</b>.
///  · 모델 하위는 <c>GrabbableItem.RemoveFromWorld</c> 의 <c>GetComponentsInChildren</c>
///    범위라, 장래에 Renderer 계열을 추가하면 획득 순간 함께 꺼진다.
///  · 상자(<c>ItemBox_low</c>)는 보청기·약통이 <b>같은 프리팹</b>을 공유하므로 그 안에
///    넣으면 두 상자가 같은 문구를 띄운다.
///  · <c>Item_HearingAid</c> / <c>Item_Medicine</c> 은 프리팹 인스턴스가 아닌 순수 씬
///    오브젝트이고, 이를 참조하는 런타임 스크립트가 없어 아무도 끄지 않는다.
///
/// 안전 규칙 —
///  · Stage1 씬 외의 씬, XR Rig, <c>ItemBox_low</c>, 아이템 프리팹, 획득 스크립트를
///    수정하지 않는다. 이 도구가 쓰는 것은 새 인스턴스 2개와 그 내부뿐이다.
///  · 이미 배치돼 있으면 <b>중복 생성하지 않고</b> 설정만 다시 맞춘다.
///  · 모든 변경은 <c>Undo</c> 에 등록되므로 Ctrl+Z로 한 번에 되돌릴 수 있다.
/// </summary>
public static class Stage1ItemDescriptionPlacement
{
    private const string Stage1ScenePath = "Assets/ReBloom/Scenes/Stage1.unity";
    private const string Stage1SceneName = "Stage1";
    private const string BoardPrefabPath = "Assets/ReBloom/Prefabs/UI/ItemDescriptionBoard.prefab";
    private const string BoardObjectName = "ItemDescriptionBoard";
    private const string BoxObjectName = "ItemBox_low";
    private const string UndoLabel = "Place Stage1 Item Description Boards";
    private const string KindUndoLabel = "Set Stage1 Item Description Kind";

    // 공통 표시 설정. 두 보드에 같은 값을 넣는다.
    private const float ShowDistance = 3f;
    private const float HideHysteresis = 0.25f;
    private const float FadeDuration = 0.3f;
    private const bool Billboard = true;
    private const bool LockPitch = true;

    private const float PositionEpsilon = 0.0005f;

    /// <summary>보드 하나에 넣을 내용 전부.</summary>
    private struct BoardSpec
    {
        public string parentName;
        public ItemKind kind;
        public Vector3 localPosition;
        public string iconPath;
        public string title;
        public string description;
        public Color accent;
    }

    private static readonly BoardSpec[] Specs =
    {
        new BoardSpec
        {
            parentName = "Item_HearingAid",
            kind = ItemKind.HearingAid,
            localPosition = new Vector3(3.1416f, 1.0f, -1.8748f),
            iconPath = "Assets/ReBloom/UI/Items/Echo_HearingAid_Icon.png",
            title = "보청기",
            description =
                "30초 동안 청각 제약이 해제됩니다.\n" +
                "파트너와의 거리에 관계없이 주변의 모든 소리를 들을 수 있습니다.",
            accent = new Color32(0x4F, 0xA8, 0xE8, 0xFF),   // #4FA8E8
        },
        new BoardSpec
        {
            parentName = "Item_Medicine",
            kind = ItemKind.Medicine,
            localPosition = new Vector3(-4.4303f, 1.0f, 4.9492f),
            iconPath = "Assets/ReBloom/UI/Items/Lumen_Medicine_Icon.png",
            title = "약통",
            description =
                "30초 동안 시각 제약이 해제됩니다.\n" +
                "파트너와의 거리에 관계없이 시각 제약 효과가 일시적으로 해제됩니다.",
            accent = new Color32(0xA7, 0x8B, 0xFA, 0xFF),   // #A78BFA
        },
    };

    [MenuItem("Re_Bloom/Items/Stage1 Item Description - Dry Run", priority = 330)]
    private static void DryRun() => Run(false);

    [MenuItem("Re_Bloom/Items/Stage1 Item Description - Apply", priority = 331)]
    private static void Apply() => Run(true);

    [MenuItem("Re_Bloom/Items/Stage1 Item Description - Validate", priority = 332)]
    private static void ValidateMenu()
    {
        if (!TryResolveScene(false, out Scene scene))
            return;

        Validate(scene);
    }

    /// <summary>
    /// 이미 배치된 보드의 <c>kind</c> 만 채운다.
    ///
    /// 전체 Apply는 위치·문구·아이콘·강조색까지 다시 쓰기 때문에, 실기에서 손으로 다듬은
    /// 값이 있으면 되돌아갈 수 있다. 획득 게이트만 추가하는 지금 단계에서는 이쪽을 쓴다.
    /// </summary>
    [MenuItem("Re_Bloom/Items/Stage1 Item Description - Apply Kind Only", priority = 333)]
    private static void ApplyKindOnly()
    {
        Debug.Log("===== Stage1 아이템 설명 보드 [APPLY KIND ONLY] =====");

        if (!TryResolveScene(true, out Scene scene))
            return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(KindUndoLabel);

        int changed = 0;
        bool failed = false;

        foreach (BoardSpec spec in Specs)
        {
            Debug.Log($"--- {spec.parentName} ---");

            if (!TryFindUnique(scene, spec.parentName, out GameObject parent))
            {
                failed = true;
                continue;
            }

            Transform boardTransform = parent.transform.Find(BoardObjectName);

            if (boardTransform == null)
            {
                Debug.LogError(
                    $"  ✗ '{BoardObjectName}' 이 없습니다. 먼저 Apply로 배치하세요.", parent);
                failed = true;
                continue;
            }

            var board = boardTransform.GetComponent<ItemDescriptionBoard>();

            if (board == null)
            {
                Debug.LogError("  ✗ ItemDescriptionBoard 컴포넌트가 없습니다.", boardTransform);
                failed = true;
                continue;
            }

            if (board.Kind == spec.kind)
            {
                Debug.Log($"  = kind가 이미 {spec.kind} 입니다. 바꾸지 않습니다.", board);
                continue;
            }

            var so = new SerializedObject(board);

            if (!SetKindField(so, spec.kind))
            {
                failed = true;
                continue;
            }

            so.ApplyModifiedProperties();   // Undo 등록 포함

            if (board.Kind != spec.kind)
            {
                Debug.LogError(
                    $"  ✗ kind 적용을 되읽지 못했습니다 (현재 {board.Kind}, 기대 {spec.kind}).", board);
                failed = true;
                continue;
            }

            changed++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);

            if (EditorSceneManager.SaveScene(scene))
                Debug.Log($"씬을 저장했습니다: {scene.path}");
            else
                Debug.LogWarning($"씬 저장에 실패했습니다. 직접 저장하세요: {scene.path}");
        }
        else
        {
            Debug.Log("바꾼 것이 없어 저장하지 않았습니다.");
        }

        Debug.Log($"kind 적용 완료 — {changed}개 변경" + (failed ? "  (문제 있음, 위 로그 확인)" : ""));

        Validate(scene);
    }

    // ───────────────────────────── 실행 ─────────────────────────────

    private static void Run(bool apply)
    {
        string mode = apply ? "APPLY" : "DRY RUN";
        Debug.Log($"===== Stage1 아이템 설명 보드 배치 [{mode}] =====");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoardPrefabPath);

        if (prefab == null)
        {
            Debug.LogError(
                $"프리팹이 없습니다: {BoardPrefabPath}\n" +
                "'Re_Bloom ▸ Items ▸ Item Description Board Prefab - Create' 를 먼저 실행하세요.");
            return;
        }

        if (prefab.GetComponent<ItemDescriptionBoard>() == null)
        {
            Debug.LogError($"프리팹에 ItemDescriptionBoard 컴포넌트가 없습니다: {BoardPrefabPath}");
            return;
        }

        if (!TryResolveScene(apply, out Scene scene))
            return;

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);

        int placed = 0;
        int updated = 0;
        bool failed = false;

        foreach (BoardSpec spec in Specs)
        {
            Debug.Log($"--- {spec.parentName} ---");

            if (!TryFindUnique(scene, spec.parentName, out GameObject parent))
            {
                failed = true;
                continue;
            }

            Transform box = parent.transform.Find(BoxObjectName);

            if (box == null)
            {
                Debug.LogError(
                    $"  ✗ '{spec.parentName}/{BoxObjectName}' 을 찾지 못했습니다. " +
                    "anchor를 연결할 수 없습니다.", parent);
                failed = true;
                continue;
            }

            Transform existing = parent.transform.Find(BoardObjectName);

            if (existing != null)
            {
                Debug.Log($"  = '{BoardObjectName}' 이미 있습니다. 중복 생성하지 않고 설정만 맞춥니다.",
                    existing.gameObject);
            }
            else
            {
                Debug.Log($"  + '{BoardObjectName}' 생성 (프리팹 인스턴스)", parent);
            }

            if (!apply)
            {
                ReportPlan(spec, box);
                continue;
            }

            GameObject instance;

            if (existing != null)
            {
                instance = existing.gameObject;
                updated++;
            }
            else
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);

                if (instance == null)
                {
                    Debug.LogError("  ✗ 프리팹 인스턴스 생성에 실패했습니다.", parent);
                    failed = true;
                    continue;
                }

                Undo.RegisterCreatedObjectUndo(instance, UndoLabel);
                instance.name = BoardObjectName;
                placed++;
            }

            if (!Configure(instance, spec, box))
                failed = true;
        }

        Undo.CollapseUndoOperations(undoGroup);

        if (!apply)
        {
            Debug.Log("DRY RUN이므로 씬을 바꾸지 않았습니다. 배치하려면 Apply를 실행하세요.");
            return;
        }

        EditorSceneManager.MarkSceneDirty(scene);

        if (EditorSceneManager.SaveScene(scene))
            Debug.Log($"씬을 저장했습니다: {scene.path}");
        else
            Debug.LogWarning($"씬 저장에 실패했습니다. 직접 저장하세요: {scene.path}");

        Debug.Log($"배치 완료 — 신규 {placed}개 / 갱신 {updated}개" +
                  (failed ? "  (문제 있음, 위 로그 확인)" : ""));

        Validate(scene);
    }

    /// <summary>
    /// 인스턴스 하나의 Transform·참조·문구·아이콘·강조색을 모두 채운다.
    /// </summary>
    private static bool Configure(GameObject instance, BoardSpec spec, Transform box)
    {
        bool ok = true;

        // ── Transform ───────────────────────────────────────────
        // 회전과 스케일은 프리팹 값(단위 회전 / 0.0012)을 그대로 둔다.
        // 회전은 런타임에 Billboard가 매 프레임 덮어쓴다.
        Undo.RecordObject(instance.transform, UndoLabel);
        instance.transform.localPosition = spec.localPosition;
        instance.transform.localRotation = Quaternion.identity;

        var board = instance.GetComponent<ItemDescriptionBoard>();

        if (board == null)
        {
            Debug.LogError("  ✗ 인스턴스에 ItemDescriptionBoard가 없습니다.", instance);
            return false;
        }

        var so = new SerializedObject(board);

        // ── anchor (거리 기준 = 상자) ───────────────────────────
        ok &= SetObjectField(so, "anchor", box, "anchor");

        // ── 아이템 종류 (획득 판정의 기준) ──────────────────────
        ok &= SetKindField(so, spec.kind);

        // ── 표시 수치 ───────────────────────────────────────────
        ok &= SetFloatField(so, "showDistance", ShowDistance);
        ok &= SetFloatField(so, "hideHysteresis", HideHysteresis);
        ok &= SetFloatField(so, "fadeDuration", FadeDuration);
        ok &= SetBoolField(so, "billboard", Billboard);
        ok &= SetBoolField(so, "lockPitch", LockPitch);

        // ── 강조색 ──────────────────────────────────────────────
        SerializedProperty accentProperty = so.FindProperty("accentColor");

        if (accentProperty == null)
        {
            Debug.LogError("  ✗ ItemDescriptionBoard에 'accentColor' 필드가 없습니다.", instance);
            ok = false;
        }
        else
        {
            accentProperty.colorValue = spec.accent;
            Debug.Log($"  ~ accentColor: #{ColorUtility.ToHtmlStringRGB(spec.accent)}", instance);
        }

        // 강조색이 적용될 Graphic들을 Undo에 먼저 등록한다.
        // (ApplyModifiedProperties → OnValidate → ApplyAccent 가 색을 바꾼다)
        List<Object> accentTargets = CollectAccentGraphics(so);

        if (accentTargets.Count > 0)
            Undo.RecordObjects(accentTargets.ToArray(), UndoLabel);

        so.ApplyModifiedProperties();   // Undo 등록 포함

        // OnValidate가 돌지 않는 경로를 대비해 직접 한 번 더 적용한다.
        board.ApplyAccent();

        foreach (Object target in accentTargets)
            EditorUtility.SetDirty(target);

        // ── 아이콘 스프라이트 ───────────────────────────────────
        Image iconImage = ResolveImage(so, "iconImage", instance, "Icon");

        if (iconImage == null)
        {
            ok = false;
        }
        else
        {
            Sprite sprite = LoadSprite(spec.iconPath);

            if (sprite == null)
            {
                Debug.LogError($"  ✗ 스프라이트를 불러오지 못했습니다: {spec.iconPath}", instance);
                ok = false;
            }
            else
            {
                Undo.RecordObject(iconImage, UndoLabel);
                iconImage.sprite = sprite;
                iconImage.preserveAspect = true;
                iconImage.raycastTarget = false;
                EditorUtility.SetDirty(iconImage);
                Debug.Log($"  ~ Icon.sprite: {sprite.name}", iconImage);
            }
        }

        // ── 문구 ────────────────────────────────────────────────
        ok &= SetText(so, "titleText", instance, "Title", spec.title);
        ok &= SetText(so, "descriptionText", instance, "Description", spec.description);

        return ok;
    }

    // ───────────────────────────── 검증 ─────────────────────────────

    private static void Validate(Scene scene)
    {
        Debug.Log("===== Stage1 아이템 설명 보드 [VALIDATE] =====");

        int problems = 0;

        foreach (BoardSpec spec in Specs)
        {
            Debug.Log($"--- {spec.parentName} ---");

            if (!TryFindUnique(scene, spec.parentName, out GameObject parent))
            {
                problems++;
                continue;
            }

            Transform box = parent.transform.Find(BoxObjectName);
            Transform boardTransform = parent.transform.Find(BoardObjectName);

            if (boardTransform == null)
            {
                Debug.LogError($"  ✗ '{BoardObjectName}' 이 없습니다. Apply를 실행하세요.", parent);
                problems++;
                continue;
            }

            GameObject instance = boardTransform.gameObject;

            // 중복 배치 검사
            int duplicates = 0;

            foreach (Transform child in parent.transform)
            {
                if (child.name == BoardObjectName)
                    duplicates++;
            }

            problems += Expect(duplicates == 1,
                $"'{BoardObjectName}' 1개 (현재 {duplicates}개)", instance);

            // 프리팹 연결
            problems += Expect(PrefabUtility.GetCorrespondingObjectFromSource(instance) != null,
                "프리팹 연결 유지됨", instance);

            // Transform
            Vector3 local = instance.transform.localPosition;
            problems += Expect(
                (local - spec.localPosition).sqrMagnitude <= PositionEpsilon * PositionEpsilon,
                $"Local Position = {spec.localPosition} (현재 {local})", instance);

            Debug.Log($"  · World Position = {instance.transform.position}", instance);

            if (box != null)
            {
                float distance = Vector3.Distance(instance.transform.position, box.position);
                Debug.Log($"  · 상자와의 거리 = {distance:F3} m  (상자 world {box.position})", instance);
            }

            // 컴포넌트 설정
            var canvas = instance.GetComponent<Canvas>();
            problems += Expect(canvas != null && canvas.renderMode == RenderMode.WorldSpace,
                "Canvas = World Space", instance);
            problems += Expect(canvas != null && canvas.sortingOrder == 1,
                $"Canvas.sortingOrder = 1 (현재 {(canvas == null ? "없음" : canvas.sortingOrder.ToString())})",
                instance);

            var group = instance.GetComponent<CanvasGroup>();
            problems += Expect(group != null && Mathf.Approximately(group.alpha, 0f),
                "CanvasGroup.alpha = 0", instance);
            problems += Expect(group != null && !group.interactable && !group.blocksRaycasts,
                "CanvasGroup interactable / blocksRaycasts = false", instance);

            // XR 안전
            problems += Expect(instance.GetComponentInChildren<GraphicRaycaster>(true) == null,
                "GraphicRaycaster 없음", instance);
            problems += Expect(instance.GetComponentInChildren<Collider>(true) == null,
                "Collider 없음", instance);

            foreach (Graphic graphic in instance.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic.raycastTarget)
                {
                    Debug.LogError($"  ✗ raycastTarget이 켜져 있습니다: {graphic.name}", graphic);
                    problems++;
                }
            }

            // 스크립트 참조와 값
            var board = instance.GetComponent<ItemDescriptionBoard>();
            problems += Expect(board != null, "ItemDescriptionBoard 있음", instance);

            if (board == null)
                continue;

            var so = new SerializedObject(board);

            problems += ExpectRef(so, "canvasGroup");
            problems += ExpectRef(so, "iconImage");
            problems += ExpectRef(so, "titleText");
            problems += ExpectRef(so, "descriptionText");

            SerializedProperty anchor = so.FindProperty("anchor");
            bool anchorOk = anchor != null && box != null
                            && anchor.objectReferenceValue == box;
            problems += Expect(anchorOk,
                $"anchor → {spec.parentName}/{BoxObjectName} " +
                $"(현재 {(anchor == null || anchor.objectReferenceValue == null ? "비어 있음" : anchor.objectReferenceValue.name)})",
                instance);

            // 획득 게이트의 기준. None이면 보드가 영원히 뜨지 않으므로 에러로 잡는다.
            problems += Expect(board.Kind == spec.kind,
                $"kind = {spec.kind} (현재 {board.Kind})", instance);

            problems += ExpectFloat(so, "showDistance", ShowDistance);
            problems += ExpectFloat(so, "hideHysteresis", HideHysteresis);
            problems += ExpectFloat(so, "fadeDuration", FadeDuration);
            problems += ExpectBool(so, "billboard", Billboard);
            problems += ExpectBool(so, "lockPitch", LockPitch);

            SerializedProperty accent = so.FindProperty("accentColor");
            bool accentOk = accent != null && SameRgb(accent.colorValue, spec.accent);
            problems += Expect(accentOk,
                $"accentColor = #{ColorUtility.ToHtmlStringRGB(spec.accent)} " +
                $"(현재 {(accent == null ? "필드 없음" : "#" + ColorUtility.ToHtmlStringRGB(accent.colorValue))})",
                instance);

            SerializedProperty accents = so.FindProperty("accentGraphics");
            problems += Expect(accents != null && accents.arraySize == 5,
                $"accentGraphics 5개 (현재 {(accents == null ? "필드 없음" : accents.arraySize.ToString())})",
                instance);

            // 강조색이 실제로 테두리에 반영됐는지
            if (accents != null)
            {
                for (int i = 0; i < accents.arraySize; i++)
                {
                    var graphic = accents.GetArrayElementAtIndex(i).objectReferenceValue as Graphic;

                    if (graphic == null)
                    {
                        Debug.LogError($"  ✗ accentGraphics[{i}] 가 비어 있습니다.", instance);
                        problems++;
                        continue;
                    }

                    if (!SameRgb(graphic.color, spec.accent))
                    {
                        Debug.LogError(
                            $"  ✗ {graphic.name} 색이 #{ColorUtility.ToHtmlStringRGB(graphic.color)} " +
                            $"입니다 (기대 #{ColorUtility.ToHtmlStringRGB(spec.accent)})", graphic);
                        problems++;
                    }
                }
            }

            // 아이콘 / 문구
            var iconImage = so.FindProperty("iconImage")?.objectReferenceValue as Image;
            string expectedIcon = System.IO.Path.GetFileNameWithoutExtension(spec.iconPath);
            problems += Expect(iconImage != null && iconImage.sprite != null
                               && iconImage.sprite.name == expectedIcon,
                $"Icon.sprite = {expectedIcon} " +
                $"(현재 {(iconImage == null || iconImage.sprite == null ? "비어 있음" : iconImage.sprite.name)})",
                instance);
            problems += Expect(iconImage != null && iconImage.preserveAspect,
                "Icon.preserveAspect = true", instance);

            problems += ExpectText(so, "titleText", spec.title, "Title");
            problems += ExpectText(so, "descriptionText", spec.description, "Description");

            // 폰트
            foreach (TMP_Text text in instance.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null)
                {
                    Debug.LogError($"  ✗ {text.name}: Font Asset이 비어 있습니다.", text);
                    problems++;
                }
            }

            // 기존 아이템 기능이 그대로인지 (이 도구가 건드리지 않았음을 확인)
            var grab = parent.GetComponentInChildren<GrabbableItem>(true);
            var prompt = parent.GetComponentInChildren<ItemPickupPromptUI>(true);
            Debug.Log($"  · 기존 유지 확인 — GrabbableItem {(grab != null ? "있음" : "없음")}, " +
                      $"ItemPickupPromptUI {(prompt != null ? "있음" : "없음")}", parent);
        }

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음."
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    // ───────────────────────────── 씬 / 탐색 ─────────────────────────────

    /// <summary>
    /// Stage1 씬을 해석한다. <paramref name="allowOpen"/> 이면 열려 있지 않을 때 열어 준다
    /// (열기 전에 Unity가 저장 여부를 묻는다 — 작업 중인 다른 씬의 변경이 날아가지 않는다).
    /// </summary>
    private static bool TryResolveScene(bool allowOpen, out Scene scene)
    {
        scene = SceneManager.GetActiveScene();

        if (scene.name == Stage1SceneName)
            return true;

        if (!allowOpen)
        {
            Debug.LogError(
                $"활성 씬이 '{scene.name}' 입니다. '{Stage1SceneName}' 씬을 열고 다시 실행하세요. " +
                "(Apply는 씬을 자동으로 엽니다)");
            return false;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("씬 열기를 취소했습니다. 아무것도 바꾸지 않았습니다.");
            return false;
        }

        Debug.Log($"'{Stage1SceneName}' 씬을 엽니다: {Stage1ScenePath}");
        scene = EditorSceneManager.OpenScene(Stage1ScenePath, OpenSceneMode.Single);

        if (scene.name != Stage1SceneName)
        {
            Debug.LogError($"씬을 열지 못했습니다: {Stage1ScenePath}");
            return false;
        }

        return true;
    }

    /// <summary>씬에서 이름이 같은 오브젝트를 찾는다. 둘 이상이면 실패로 본다.</summary>
    private static bool TryFindUnique(Scene scene, string objectName, out GameObject found)
    {
        var matches = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == objectName)
                matches.Add(root);

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == objectName && child.gameObject != root)
                    matches.Add(child.gameObject);
            }
        }

        if (matches.Count == 1)
        {
            found = matches[0];
            return true;
        }

        found = null;

        if (matches.Count == 0)
            Debug.LogError($"  ✗ '{objectName}' 을 씬에서 찾지 못했습니다.");
        else
            Debug.LogError($"  ✗ '{objectName}' 이 {matches.Count}개 있습니다. 하나만 남기세요.");

        return false;
    }

    // ───────────────────────────── 필드 설정 ─────────────────────────────

    private static bool SetObjectField(
        SerializedObject so, string fieldName, Object value, string label)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ ItemDescriptionBoard에 '{fieldName}' 필드가 없습니다.");
            return false;
        }

        property.objectReferenceValue = value;
        Debug.Log($"  ~ {label}: {(value == null ? "(없음)" : value.name)}");
        return true;
    }

    private static bool SetFloatField(SerializedObject so, string fieldName, float value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ ItemDescriptionBoard에 '{fieldName}' 필드가 없습니다.");
            return false;
        }

        property.floatValue = value;
        return true;
    }

    /// <summary>
    /// <c>kind</c> 를 쓴다. ItemKind는 None=0 / HearingAid=1 / Medicine=2 로 값이 연속이므로
    /// <c>enumValueIndex</c>(이름 배열의 인덱스)와 실제 값이 일치한다.
    /// 그래도 혼동을 피하려고 쓴 뒤 컴포넌트의 <c>Kind</c> 프로퍼티로 되읽어 확인한다.
    /// </summary>
    private static bool SetKindField(SerializedObject so, ItemKind value)
    {
        SerializedProperty property = so.FindProperty("kind");

        if (property == null)
        {
            Debug.LogError("  ✗ ItemDescriptionBoard에 'kind' 필드가 없습니다.");
            return false;
        }

        property.enumValueIndex = (int)value;
        Debug.Log($"  ~ kind: {value} ({ItemKindRules.DisplayName(value)})");
        return true;
    }

    private static bool SetBoolField(SerializedObject so, string fieldName, bool value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ ItemDescriptionBoard에 '{fieldName}' 필드가 없습니다.");
            return false;
        }

        property.boolValue = value;
        return true;
    }

    /// <summary>
    /// TMP 문구를 넣는다. <c>m_text</c> 를 직접 쓰지 않고 프로퍼티로 넣어
    /// TMP가 메시를 다시 만들게 한다(에디터에서 글자가 갱신되지 않는 것을 막는다).
    /// </summary>
    private static bool SetText(
        SerializedObject so, string fieldName, GameObject instance, string childName, string value)
    {
        var text = so.FindProperty(fieldName)?.objectReferenceValue as TMP_Text;

        if (text == null)
        {
            Transform child = instance.transform.Find(childName);
            text = child != null ? child.GetComponent<TMP_Text>() : null;
        }

        if (text == null)
        {
            Debug.LogError($"  ✗ '{fieldName}' ({childName}) 의 TMP를 찾지 못했습니다.", instance);
            return false;
        }

        Undo.RecordObject(text, UndoLabel);
        text.text = value;
        text.raycastTarget = false;
        EditorUtility.SetDirty(text);

        Debug.Log($"  ~ {childName}.text: \"{value.Replace("\n", "\\n")}\"", text);
        return true;
    }

    private static Image ResolveImage(
        SerializedObject so, string fieldName, GameObject instance, string childName)
    {
        var image = so.FindProperty(fieldName)?.objectReferenceValue as Image;

        if (image != null)
            return image;

        Transform child = instance.transform.Find(childName);
        image = child != null ? child.GetComponent<Image>() : null;

        if (image == null)
            Debug.LogError($"  ✗ '{fieldName}' ({childName}) 의 Image를 찾지 못했습니다.", instance);

        return image;
    }

    private static List<Object> CollectAccentGraphics(SerializedObject so)
    {
        var targets = new List<Object>();
        SerializedProperty accents = so.FindProperty("accentGraphics");

        if (accents == null)
            return targets;

        for (int i = 0; i < accents.arraySize; i++)
        {
            Object value = accents.GetArrayElementAtIndex(i).objectReferenceValue;

            if (value != null)
                targets.Add(value);
        }

        return targets;
    }

    /// <summary>
    /// Sprite 서브에셋을 불러온다. Single 모드 텍스처의 메인 에셋은 Texture2D이므로
    /// 타입을 지정해 불러오고, 실패하면 경로의 모든 에셋에서 Sprite를 찾는다.
    /// </summary>
    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (sprite != null)
            return sprite;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is Sprite found)
                return found;
        }

        return null;
    }

    // ───────────────────────────── 검증 보조 ─────────────────────────────

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
            Debug.LogError($"  ✗ '{fieldName}' 필드가 없습니다.");
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

    private static int ExpectFloat(SerializedObject so, string fieldName, float expected)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ '{fieldName}' 필드가 없습니다.");
            return 1;
        }

        bool ok = Mathf.Approximately(property.floatValue, expected);
        return Expect(ok, $"{fieldName} = {expected} (현재 {property.floatValue})", null);
    }

    private static int ExpectBool(SerializedObject so, string fieldName, bool expected)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ '{fieldName}' 필드가 없습니다.");
            return 1;
        }

        bool ok = property.boolValue == expected;
        return Expect(ok, $"{fieldName} = {expected} (현재 {property.boolValue})", null);
    }

    private static int ExpectText(
        SerializedObject so, string fieldName, string expected, string label)
    {
        var text = so.FindProperty(fieldName)?.objectReferenceValue as TMP_Text;

        if (text == null)
        {
            Debug.LogError($"  ✗ {fieldName} 참조가 비어 있습니다.");
            return 1;
        }

        bool ok = text.text == expected;
        return Expect(ok,
            $"{label}.text 일치 (현재 \"{text.text.Replace("\n", "\\n")}\")", text);
    }

    /// <summary>알파를 무시하고 RGB만 비교한다. 테두리 조각은 알파가 서로 다르다.</summary>
    private static bool SameRgb(Color a, Color b)
    {
        const float tolerance = 0.004f;   // 8비트 1단계보다 조금 넉넉하게
        return Mathf.Abs(a.r - b.r) <= tolerance
            && Mathf.Abs(a.g - b.g) <= tolerance
            && Mathf.Abs(a.b - b.b) <= tolerance;
    }

    private static void ReportPlan(BoardSpec spec, Transform box)
    {
        Vector3 world = box.parent != null
            ? box.parent.TransformPoint(spec.localPosition)
            : spec.localPosition;

        Debug.Log(
            $"  [DRY RUN 계획]\n" +
            $"    부모            {spec.parentName}\n" +
            $"    kind            {spec.kind} ({ItemKindRules.DisplayName(spec.kind)}) " +
            "— 획득 확정 전에는 표시되지 않음\n" +
            $"    Local Position  {spec.localPosition}\n" +
            $"    World Position  {world}  (상자 {box.position}, 높이차 {world.y - box.position.y:F3} m)\n" +
            $"    anchor          {spec.parentName}/{BoxObjectName}\n" +
            $"    Icon            {spec.iconPath}\n" +
            $"    Title           {spec.title}\n" +
            $"    Description     {spec.description.Replace("\n", " / ")}\n" +
            $"    Accent Color    #{ColorUtility.ToHtmlStringRGB(spec.accent)}\n" +
            $"    표시            showDistance {ShowDistance} / hideHysteresis {HideHysteresis} / " +
            $"fadeDuration {FadeDuration} / billboard {Billboard} / lockPitch {LockPitch}");
    }
}
