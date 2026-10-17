using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;
using TMPro;

/// <summary>
/// Stage1의 아이템 모델을 손으로 잡을 수 있게 만드는 1회용 셋업 도구.
///
/// 대상 계층 (씬의 실제 구조) —
/// <code>
/// Item_HearingAid            ← 빈 부모. 건드리지 않는다
/// ├── HearingAid_low         ← 여기에 Grab을 붙인다
/// │   ├── Ear_Glass_low
/// │   └── Ear_Metal_low
/// └── ItemBox_low            ← 상자. 제외
///     └── Particle System    ← 제외
/// </code>
///
/// 왜 모델에 붙이는가:
/// 빈 부모에 붙이면 상자와 파티클까지 함께 잡히고 함께 움직인다. 모델
/// (HearingAid_low / Medicine_low)에 붙이면 상자는 제자리에 남고, 획득 시
/// 모델만 사라진다. 부모는 자식이 움직여도 따라오지 않으므로 Item_* 도 그대로 있다.
///
/// 왜 에디터 도구인가:
/// Collider 크기는 <b>실제 Renderer 바운드</b>에서 나와야 한다. 씬 YAML에 손으로
/// 쓰려면 크기를 추측해야 하므로 Unity가 아는 값을 쓰도록 도구로 만들었다.
///
/// 바운드에서 <see cref="ParticleSystemRenderer"/> 를 제외하는 이유:
/// ItemBox_low 의 Particle System 은 파티클 시뮬레이션 영역 전체를 bounds 로
/// 돌려주므로, 포함하면 Collider 가 비정상적으로 커진다. 모델 하위만 보도록
/// 범위를 좁히는 것과 함께 이중으로 막는다.
///
/// 원본 FBX 와 모델 프리팹 에셋은 수정하지 않는다. 컴포넌트는 Stage1 씬의
/// <b>프리팹 인스턴스</b>에만 추가되므로 Unity 에서는 "Added Component" 오버라이드로 남는다.
///
/// Dry Run으로 먼저 확인한 뒤 Apply를 누르는 것을 권한다. Apply는 Undo를 등록한다.
/// </summary>
public static class Stage1ItemSetup
{
    private const string Stage1SceneName = "Stage1";

    /// <summary>Fuel / GeneratorPart 와 같은 레이어 이름.</summary>
    private const string InteractableLayerName = "Interactable";

    /// <summary>안내 문구에 쓸 한글 TMP 폰트. 내레이션·StartScene과 같은 에셋이다.</summary>
    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    /// <summary>(아이템 부모, Grab을 붙일 모델, 아이템 종류)</summary>
    private static readonly (string parentName, string modelName, ItemKind kind)[] Targets =
    {
        ("Item_HearingAid", "HearingAid_low", ItemKind.HearingAid),
        ("Item_Medicine", "Medicine_low", ItemKind.Medicine),
    };

    /// <summary>부모에 잘못 붙어 있으면 경고할 컴포넌트들. (이전 버전의 결과물 감지)</summary>
    private static readonly System.Type[] MisplacedOnParent =
    {
        typeof(GrabbableItem),
        typeof(XRGrabInteractable),
        typeof(Rigidbody),
        typeof(BoxCollider),
    };

    [MenuItem("Re_Bloom/Items/Stage1 Item Setup - Dry Run", priority = 300)]
    private static void DryRun() => Run(apply: false);

    [MenuItem("Re_Bloom/Items/Stage1 Item Setup - Apply", priority = 301)]
    private static void Apply()
    {
        if (!EditorUtility.DisplayDialog(
                "Stage1 Item Setup",
                "HearingAid_low / Medicine_low 모델에\n" +
                "Rigidbody + BoxCollider + XRGrabInteractable + GrabbableItem 을 추가합니다.\n\n" +
                "· 부모(Item_*)와 ItemBox_low / Particle System 은 건드리지 않습니다.\n" +
                "· Collider 크기는 모델 Renderer 바운드로만 계산합니다.\n" +
                "· 원본 FBX / 모델 프리팹 에셋은 수정하지 않습니다.\n" +
                "· 이미 붙어 있는 컴포넌트는 건드리지 않습니다.\n" +
                "· Undo로 되돌릴 수 있지만, 먼저 Dry Run으로 확인하는 것을 권합니다.\n\n" +
                "계속할까요?",
                "Apply", "취소"))
        {
            return;
        }

        Run(apply: true);
    }

    private static void Run(bool apply)
    {
        string mode = apply ? "APPLY" : "DRY RUN";
        Debug.Log($"===== Stage1 Item Setup ({mode}) =====");

        Scene scene = SceneManager.GetActiveScene();

        if (scene.name != Stage1SceneName)
        {
            Debug.LogError(
                $"[Stage1ItemSetup] 활성 씬이 '{scene.name}' 입니다. " +
                $"'{Stage1SceneName}' 씬을 열고 다시 실행하세요.");
            return;
        }

        int changed = 0;

        foreach ((string parentName, string modelName, ItemKind kind) in Targets)
        {
            if (!TryFindUnique(scene, parentName, out GameObject parent))
                continue;

            WarnIfMisplacedOnParent(parent);

            if (!TryFindModel(parent, modelName, out GameObject model))
                continue;

            Debug.Log($"--- {parentName} / {modelName} (kind={kind}) ---", model);

            if (SetupOne(model, kind, apply))
                changed++;
        }

        if (apply && changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Stage1ItemSetup] {changed}개 오브젝트를 수정했습니다. 씬을 저장하세요 (Ctrl+S).");
        }
        else if (!apply)
        {
            Debug.Log("[Stage1ItemSetup] Dry Run 완료. 위 로그를 확인한 뒤 Apply를 실행하세요.");
        }
        else
        {
            Debug.Log("[Stage1ItemSetup] 변경할 것이 없습니다. (이미 셋업됨)");
        }
    }

    /// <summary>이름이 정확히 일치하는 루트 오브젝트 하나를 찾는다. 0개나 2개 이상이면 실패로 본다.</summary>
    private static bool TryFindUnique(Scene scene, string objectName, out GameObject found)
    {
        var matches = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == objectName)
                    matches.Add(t.gameObject);
            }
        }

        if (matches.Count == 1)
        {
            found = matches[0];
            return true;
        }

        found = null;

        Debug.LogError(
            matches.Count == 0
                ? $"[Stage1ItemSetup] '{objectName}' 을 찾지 못했습니다."
                : $"[Stage1ItemSetup] '{objectName}' 이 {matches.Count}개 있습니다. 이름이 겹치지 않게 정리하세요.");

        return false;
    }

    /// <summary>
    /// 아이템 부모 아래에서 Grab을 붙일 모델을 찾는다.
    /// 중첩 깊이를 가정하지 않고 하위 전체를 보되, 이름이 정확히 일치하는 것만 쓴다.
    /// </summary>
    private static bool TryFindModel(GameObject parent, string modelName, out GameObject found)
    {
        var matches = new List<GameObject>();

        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.gameObject != parent && t.name == modelName)
                matches.Add(t.gameObject);
        }

        if (matches.Count == 1)
        {
            found = matches[0];
            return true;
        }

        found = null;

        Debug.LogError(
            matches.Count == 0
                ? $"[Stage1ItemSetup] '{parent.name}' 아래에서 '{modelName}' 을 찾지 못했습니다. " +
                  "모델 오브젝트 이름이 바뀌었는지 확인하세요."
                : $"[Stage1ItemSetup] '{parent.name}' 아래에 '{modelName}' 이 {matches.Count}개 있습니다.",
            parent);

        return false;
    }

    /// <summary>
    /// 이전 버전의 도구가 부모(Item_*)에 컴포넌트를 붙여 뒀다면 알려 준다.
    ///
    /// 자동으로 지우지 않는다 — 다른 이유로 붙여 둔 것일 수도 있고, 컴포넌트 삭제는
    /// 되돌리기 어려운 변경이라 사용자가 보고 판단하는 편이 안전하다.
    /// </summary>
    private static void WarnIfMisplacedOnParent(GameObject parent)
    {
        foreach (System.Type type in MisplacedOnParent)
        {
            if (parent.GetComponent(type) == null)
                continue;

            Debug.LogWarning(
                $"[Stage1ItemSetup] '{parent.name}' 에 {type.Name} 이 붙어 있습니다. " +
                "이제 Grab은 모델 쪽에 붙으므로, 부모에 있는 것은 Inspector에서 직접 제거하세요. " +
                "(남겨 두면 부모와 모델이 모두 잡혀 상자까지 함께 움직일 수 있습니다)",
                parent);
        }
    }

    private static bool SetupOne(GameObject target, ItemKind kind, bool apply)
    {
        bool anyChange = false;

        // ── 바운드 계산 (Collider 크기의 근거) ──────────────────
        if (!TryGetLocalBounds(target, out Bounds localBounds))
        {
            Debug.LogError(
                $"[Stage1ItemSetup] {target.name}: 하위에 Renderer가 없어 Collider 크기를 정할 수 없습니다.",
                target);
            return false;
        }

        Debug.Log(
            $"  바운드(로컬): center={localBounds.center}, size={localBounds.size}",
            target);

        // ── Layer ───────────────────────────────────────────────
        // Fuel / GeneratorPart 와 같은 Interactable 레이어로 통일한다.
        // (Collider는 루트에 있으므로 루트만 바꾸면 물리에 충분하다 — Fuel도 루트만 8이다)
        int interactableLayer = LayerMask.NameToLayer(InteractableLayerName);

        if (interactableLayer < 0)
        {
            Debug.LogWarning(
                $"[Stage1ItemSetup] '{InteractableLayerName}' 레이어를 찾지 못해 Layer를 바꾸지 않았습니다.",
                target);
        }
        else
        {
            anyChange |= SetField(target, "Layer", target.layer, interactableLayer,
                apply, () => target.layer = interactableLayer);
        }

        // ── Rigidbody ───────────────────────────────────────────
        // Fuel.prefab / GeneratorPart.prefab 과 동일하게 맞춘다.
        // 중력을 켜고 바닥에 얹혀 있게 두는 것이 이 프로젝트에서 검증된 방식이다 —
        // 중력을 끄면 감쇠가 없는 무중력 강체가 되어 한 번 받은 충격으로 영원히 표류한다.
        var body = target.GetComponent<Rigidbody>();

        if (body == null)
        {
            Debug.Log("  + Rigidbody", target);
            anyChange = true;

            if (apply)
                body = Undo.AddComponent<Rigidbody>(target);
        }
        else
        {
            Debug.Log("  = Rigidbody 이미 있음 (설정만 정규화)", target);
        }

        if (apply && body != null)
            Undo.RecordObject(body, "Stage1 Item Rigidbody");

        anyChange |= SetField(target, "Rigidbody.useGravity", body != null && body.useGravity, true,
            apply, () => body.useGravity = true);

        anyChange |= SetField(target, "Rigidbody.isKinematic", body != null && body.isKinematic, false,
            apply, () => body.isKinematic = false);

        anyChange |= SetField(target, "Rigidbody.mass", body != null ? body.mass : 0f, 1f,
            apply, () => body.mass = 1f);

        // Linear Damping 0 / Angular Damping 0.05 — Unity 기본값이자 Fuel의 값이다.
        anyChange |= SetField(target, "Rigidbody.linearDamping", body != null ? body.linearDamping : -1f, 0f,
            apply, () => body.linearDamping = 0f);

        anyChange |= SetField(target, "Rigidbody.angularDamping", body != null ? body.angularDamping : -1f, 0.05f,
            apply, () => body.angularDamping = 0.05f);

        anyChange |= SetField(target, "Rigidbody.collisionDetectionMode",
            body != null ? body.collisionDetectionMode : default,
            CollisionDetectionMode.Discrete,
            apply, () => body.collisionDetectionMode = CollisionDetectionMode.Discrete);

        anyChange |= SetField(target, "Rigidbody.interpolation",
            body != null ? body.interpolation : default,
            RigidbodyInterpolation.None,
            apply, () => body.interpolation = RigidbodyInterpolation.None);

        // ── BoxCollider ─────────────────────────────────────────
        // 이전 버전 도구가 파티클 바운드까지 포함해 거대한 박스를 만들었을 수 있다.
        // 이미 있어도 모델 바운드로 다시 맞춘다. (그래야 손이 상자 안에서 막히지 않는다)
        var box = target.GetComponent<BoxCollider>();

        if (box == null)
        {
            Debug.Log("  + BoxCollider", target);
            anyChange = true;

            if (apply)
                box = Undo.AddComponent<BoxCollider>(target);
        }
        else
        {
            Debug.Log($"  = BoxCollider 이미 있음 (현재 size={box.size}, center={box.center})", target);

            // 모델 지름 대비 3배를 넘으면 거의 확실히 파티클/상자가 섞인 값이다.
            float modelMax = Mathf.Max(localBounds.size.x, localBounds.size.y, localBounds.size.z);
            float currentMax = Mathf.Max(box.size.x, box.size.y, box.size.z);

            if (modelMax > 0f && currentMax > modelMax * 3f)
            {
                Debug.LogWarning(
                    $"  ! BoxCollider가 모델보다 {currentMax / modelMax:F1}배 큽니다. " +
                    "이전 버전 도구가 파티클 바운드를 포함해 만든 값으로 보입니다. 모델 크기로 교체합니다.",
                    target);
            }
        }

        if (apply && box != null)
            Undo.RecordObject(box, "Stage1 Item BoxCollider");

        anyChange |= SetField(target, "BoxCollider.center",
            box != null ? box.center : default, localBounds.center,
            apply, () => box.center = localBounds.center);

        anyChange |= SetField(target, "BoxCollider.size",
            box != null ? box.size : default, localBounds.size,
            apply, () => box.size = localBounds.size);

        anyChange |= SetField(target, "BoxCollider.isTrigger",
            box != null && box.isTrigger, false,
            apply, () => box.isTrigger = false);

        // ── XRGrabInteractable ──────────────────────────────────
        var grab = target.GetComponent<XRGrabInteractable>();

        if (grab == null)
        {
            Debug.Log("  + XRGrabInteractable", target);
            anyChange = true;

            if (apply)
                grab = Undo.AddComponent<XRGrabInteractable>(target);
        }
        else
        {
            Debug.Log("  = XRGrabInteractable 이미 있음 (설정만 정규화)", target);
        }

        if (apply && grab != null)
            Undo.RecordObject(grab, "Stage1 Item XRGrabInteractable");

        // 아래 4개는 Fuel.prefab / GeneratorPart.prefab 의 값과 동일하게 맞춘다.

        // Kinematic: 잡는 동안 Rigidbody로 움직여 지오메트리를 뚫지 않는다.
        anyChange |= SetField(target, "XRGrab.movementType",
            grab != null ? grab.movementType : default,
            XRBaseInteractable.MovementType.Kinematic,
            apply, () => grab.movementType = XRBaseInteractable.MovementType.Kinematic);

        // useGravity가 이미 true이므로 강제로 켤 필요가 없다.
        // (XRI는 놓을 때 잡기 전 값으로 복원한다 → 그대로 중력 낙하)
        anyChange |= SetField(target, "XRGrab.forceGravityOnDetach",
            grab != null && grab.forceGravityOnDetach, false,
            apply, () => grab.forceGravityOnDetach = false);

        anyChange |= SetField(target, "XRGrab.throwOnDetach",
            grab != null && grab.throwOnDetach, true,
            apply, () => grab.throwOnDetach = true);

        anyChange |= SetField(target, "XRGrab.throwVelocityScale",
            grab != null ? grab.throwVelocityScale : -1f, 1.5f,
            apply, () => grab.throwVelocityScale = 1.5f);

        anyChange |= SetField(target, "XRGrab.retainTransformParent",
            grab != null && grab.retainTransformParent, false,
            apply, () => grab.retainTransformParent = false);

        // ── XRGeneralGrabTransformer ────────────────────────────
        // Fuel / GeneratorPart 가 명시적으로 갖고 있는 컴포넌트다.
        // 없으면 XRI가 런타임에 기본 Transformer를 추가하므로 동작은 하지만,
        // 프리팹에 박아 두면 잡는 느낌이 기존 아이템과 완전히 같아진다.
        // XRGrabInteractable이 자동 등록하므로 별도 배선은 필요하지 않다.
        var transformer = target.GetComponent<XRGeneralGrabTransformer>();

        if (transformer == null)
        {
            Debug.Log("  + XRGeneralGrabTransformer", target);
            anyChange = true;

            if (apply)
                Undo.AddComponent<XRGeneralGrabTransformer>(target);
        }
        else
        {
            Debug.Log("  = XRGeneralGrabTransformer 이미 있음 (설정 건드리지 않음)", target);
        }

        // ── 획득 안내 UI ────────────────────────────────────────
        anyChange |= SetupPrompt(target, apply);

        // ── GrabbableItem ───────────────────────────────────────
        var item = target.GetComponent<GrabbableItem>();

        if (item == null)
        {
            Debug.Log($"  + GrabbableItem (kind={kind})", target);
            anyChange = true;

            if (apply)
            {
                item = Undo.AddComponent<GrabbableItem>(target);
                SetKind(item, kind);
            }
        }
        else
        {
            Debug.Log($"  = GrabbableItem 이미 있음 (Kind={item.Kind})", target);

            if (item.Kind != kind)
            {
                Debug.LogWarning(
                    $"  ! GrabbableItem.Kind 가 {item.Kind} 입니다. 기대값은 {kind} 입니다. " +
                    "인스펙터에서 확인하세요.", target);
            }
        }

        return anyChange;
    }

    /// <summary>
    /// 획득 안내 UI(World Space Canvas + TMP)를 아이템 <b>자식</b>으로 만든다.
    ///
    /// 이미 있으면 다시 만들지 않는다(요구: 중복 생성 금지). 이름으로 찾는다.
    ///
    /// XR 입력을 방해하지 않도록 —
    ///   · Canvas에 <c>GraphicRaycaster</c> 를 <b>붙이지 않는다</b> → UI Ray가 이 캔버스를 보지 않는다
    ///   · TMP의 <c>raycastTarget</c> 을 false 로 둔다
    ///   · Collider를 만들지 않으므로 물리 기반 Grab 판정에도 걸리지 않는다
    ///
    /// 바운드 계산에 영향이 없는 이유:
    /// World Space Canvas와 TMP는 <c>CanvasRenderer</c> 로 그려지며 <c>Renderer</c> 컴포넌트가
    /// 아니다. 그래서 <see cref="TryGetLocalBounds"/> 의 Renderer 수집에 끼지 않고,
    /// GrabbableItem이 획득 시 끄는 renderers / colliders 목록에도 들어가지 않는다.
    /// </summary>
    private static bool SetupPrompt(GameObject target, bool apply)
    {
        const string canvasName = "PickupPrompt";
        const string textName = "PromptText";

        bool anyChange = false;

        // ── ItemPickupPromptUI ──────────────────────────────────
        var prompt = target.GetComponent<ItemPickupPromptUI>();

        if (prompt == null)
        {
            Debug.Log("  + ItemPickupPromptUI", target);
            anyChange = true;

            if (apply)
                prompt = Undo.AddComponent<ItemPickupPromptUI>(target);
        }
        else
        {
            Debug.Log("  = ItemPickupPromptUI 이미 있음", target);
        }

        // ── 캔버스 ──────────────────────────────────────────────
        Transform existing = target.transform.Find(canvasName);

        if (existing != null)
        {
            Debug.Log($"  = '{canvasName}' 이미 있음 (다시 만들지 않음)", existing.gameObject);
            return anyChange;
        }

        Debug.Log($"  + '{canvasName}' (World Space Canvas + TMP, GraphicRaycaster 없음)", target);

        if (!apply)
            return true;

        // 캔버스 루트
        var canvasObject = new GameObject(canvasName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Create Pickup Prompt");
        Undo.SetTransformParent(canvasObject.transform, target.transform, "Create Pickup Prompt");

        canvasObject.layer = target.layer;

        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.localPosition = Vector3.zero;
        canvasRect.localRotation = Quaternion.identity;
        canvasRect.localScale = Vector3.one * 0.0012f;   // 런타임에 스크립트가 다시 보정한다
        canvasRect.anchorMin = new Vector2(0.5f, 0.5f);
        canvasRect.anchorMax = new Vector2(0.5f, 0.5f);
        canvasRect.pivot = new Vector2(0.5f, 0.5f);
        canvasRect.sizeDelta = new Vector2(600f, 120f);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        // GraphicRaycaster 를 일부러 넣지 않는다. CanvasScaler 도 World Space에서는 불필요하다.

        var group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        // 텍스트
        var textObject = new GameObject(textName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(textObject, "Create Pickup Prompt");
        Undo.SetTransformParent(textObject.transform, canvasObject.transform, "Create Pickup Prompt");

        textObject.layer = target.layer;

        var textRect = (RectTransform)textObject.transform;
        textRect.localPosition = Vector3.zero;
        textRect.localRotation = Quaternion.identity;
        textRect.localScale = Vector3.one;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var tmp = textObject.AddComponent<TextMeshProUGUI>();
        tmp.text = "Y 버튼을 눌러 획득";
        tmp.fontSize = 44f;
        tmp.enableAutoSizing = false;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;        // UI Ray가 이 글자를 집지 않게 한다

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);

        if (font != null)
        {
            tmp.font = font;
        }
        else
        {
            Debug.LogWarning(
                $"[Stage1ItemSetup] 한글 TMP 폰트를 찾지 못했습니다: {KoreanFontPath}\n" +
                "TMP 기본 폰트에는 한글 글리프가 없어 문구가 깨질 수 있습니다. " +
                "PromptText의 Font Asset을 직접 지정하세요.", textObject);
        }

        // 스크립트에 참조 연결 (private SerializeField라 SerializedObject로 넣는다)
        if (prompt != null)
        {
            var so = new SerializedObject(prompt);
            SetObjectRef(so, "promptGroup", group, prompt);
            SetObjectRef(so, "promptText", tmp, prompt);
            so.ApplyModifiedProperties();
        }

        canvasObject.SetActive(false);   // 잡기 전에는 숨김

        return true;
    }

    private static void SetObjectRef(
        SerializedObject so, string propertyName, Object value, Object context)
    {
        SerializedProperty prop = so.FindProperty(propertyName);

        if (prop == null)
        {
            Debug.LogError(
                $"[Stage1ItemSetup] ItemPickupPromptUI 의 '{propertyName}' 필드를 찾지 못했습니다. " +
                "인스펙터에서 직접 연결하세요.", context);
            return;
        }

        prop.objectReferenceValue = value;
    }

    /// <summary>
    /// 값이 기대치와 다를 때만 바꾸고 로그를 남긴다. 같으면 아무것도 하지 않는다.
    /// Dry Run에서는 "무엇을 바꿀 것인지"만 출력한다.
    /// </summary>
    private static bool SetField<T>(
        GameObject context, string label, T current, T expected, bool apply, System.Action setter)
    {
        if (EqualityComparer<T>.Default.Equals(current, expected))
            return false;

        Debug.Log($"    ~ {label}: {current} → {expected}", context);

        if (apply)
            setter();

        return true;
    }

    /// <summary>GrabbableItem.kind 는 private SerializeField 이므로 SerializedObject로 넣는다.</summary>
    private static void SetKind(GrabbableItem item, ItemKind kind)
    {
        var so = new SerializedObject(item);
        SerializedProperty prop = so.FindProperty("kind");

        if (prop == null)
        {
            Debug.LogError(
                "[Stage1ItemSetup] GrabbableItem 의 'kind' 필드를 찾지 못했습니다. " +
                "인스펙터에서 직접 지정하세요.", item);
            return;
        }

        prop.enumValueIndex = (int)kind;
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// 대상(모델) 하위의 메시 Renderer만 감싸는 바운드를 대상의 로컬 공간으로 구한다.
    ///
    /// 범위가 모델 하위로 좁혀져 있으므로 형제인 ItemBox_low 와 그 Particle System 은
    /// 애초에 들어오지 않는다. 그래도 <see cref="ParticleSystemRenderer"/> 는 한 번 더
    /// 걸러낸다 — 파티클 bounds 는 시뮬레이션 영역 전체라 하나만 섞여도 박스가 폭주한다.
    ///
    /// 꺼져 있는 Renderer 의 bounds 는 신뢰할 수 없으므로 먼저 켜져 있는 것만 쓰고,
    /// 하나도 없을 때만 전체를 대상으로 되돌린다.
    /// </summary>
    private static bool TryGetLocalBounds(GameObject target, out Bounds localBounds)
    {
        localBounds = default;

        var usable = new List<Renderer>();

        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer)
            {
                Debug.Log($"    (제외) ParticleSystemRenderer: {renderer.name}", renderer);
                continue;
            }

            usable.Add(renderer);
        }

        if (usable.Count == 0)
            return false;

        // 켜져 있는 것만 우선 사용한다.
        var active = usable.FindAll(r => r.enabled && r.gameObject.activeInHierarchy);
        List<Renderer> source = active.Count > 0 ? active : usable;

        Transform targetTransform = target.transform;
        bool started = false;

        foreach (Renderer renderer in source)
        {
            Debug.Log(
                $"    (사용) {renderer.GetType().Name}: {renderer.name} " +
                $"worldSize={renderer.bounds.size}", renderer);

            // 월드 바운드의 8개 꼭짓점을 로컬로 옮겨 감싼다.
            // (회전된 자식이 있어도 빠지는 부분이 없다)
            Bounds world = renderer.bounds;
            Vector3 min = world.min;
            Vector3 max = world.max;

            for (int corner = 0; corner < 8; corner++)
            {
                var worldCorner = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);

                Vector3 local = targetTransform.InverseTransformPoint(worldCorner);

                if (!started)
                {
                    localBounds = new Bounds(local, Vector3.zero);
                    started = true;
                }
                else
                {
                    localBounds.Encapsulate(local);
                }
            }
        }

        return started;
    }
}
