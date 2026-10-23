using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Stage2 수로 미션 안내판(<see cref="MissionGuideBoard"/>)을 씬에 놓고 참조를 잇는 도구.
///
/// 왜 씬 파일을 직접 고치지 않는가:
/// <c>Stage2.unity</c> 디스크 파일에는 안내판 인스턴스가 <b>아직 들어 있지 않다</b>.
/// 에디터에서 배치한 뒤 저장하지 않았기 때문이다. 이 상태에서 YAML을 직접 고치면
/// 사용자가 Ctrl+S 를 누르는 순간 에디터의 메모리 상태가 파일을 덮어써 작업이 사라진다.
/// 그래서 "열려 있는 씬"을 대상으로 동작하는 에디터 도구로 만들었다.
///
/// 하는 일 —
///  ① 열린 씬 중 Stage2 를 찾는다.
///  ② 그 씬의 <see cref="MissionGuideBoard"/> 인스턴스를 찾는다.
///     없으면 프리팹을 하나 놓고, 둘 이상이면 <b>멈춘다</b>(어느 것을 남길지 추측하지 않는다).
///  ③ <c>WaterwayZone</c> 기준으로 위치·회전을 계산해 적용한다. 크기(Scale)는 건드리지 않는다.
///  ④ <c>anchor</c> 에 <c>WaterwayZone</c> 을 잇고 표시 조건 값들을 채운다.
///
/// 거리 기준점과 표시 위치가 따로 노는 이유:
/// <see cref="MissionGuideBoard"/> 는 거리를 <c>anchor.position</c> 에서 재고
/// (<c>UpdateVisibility</c>), 빌보드 회전은 <c>transform.position</c> 을 쓴다. 두 값이
/// 서로 독립이라 "수로 한가운데를 기준으로 거리를 재되, 판은 물 밖에 세운다"가 가능하다.
///
/// 이 도구가 <b>건드리지 않는 것</b> — 미션 판정, Fusion 네트워크, 다른 씬, 프리팹 에셋,
/// 안내판의 Scale과 연결된 캐릭터 Sprite.
/// 씬을 자동 저장하지도 않는다. 결과를 눈으로 확인한 뒤 직접 저장하면 되고,
/// 마음에 들지 않으면 Ctrl+Z 로 되돌릴 수 있다.
/// </summary>
public static class Stage2MissionGuidePlacement
{
    private const string SceneName = "Stage2";
    private const string PrefabPath = "Assets/ReBloom/Prefabs/UI/MissionGuideBoard.prefab";
    private const string AnchorName = "WaterwayZone";
    private const string InstanceName = "MissionGuideBoard";

    // ── 배치 규칙 ─────────────────────────────────────────────────────────
    //
    // WaterwayZoneVisual 이 그리는 경계선은 arcCenterAngle 230° / arcAngle 180° 라
    // 140°~320° 구간에만 선이 있다. 선이 없는 쪽(320°~140°, 가운데 50°)이 열린 면이고
    // 플레이어가 서는 쪽이다. 그래서 그 방향으로 판을 세운다.
    // (각도 기준은 WaterwayZoneVisual 주석과 같다 — 0° = +X, 90° = +Z)
    private const float PlacementAngleDegrees = 50f;

    // 수로 판정 반경이 1.4 m 다. 2.8 m 면 작업 구역 밖이면서,
    // 2.10 m 높이의 판을 올려다보는 각도가 완만해지는 거리다(윗변까지 약 19°).
    private const float PlacementDistance = 2.8f;

    // 지면에서 판 중심까지.
    //
    // 기준 지면은 "판이 설 자리"가 아니라 "플레이어가 서는 자리"(= anchor의 XZ)다.
    // 수로는 파인 지형이라 판이 둑 위에 서면 그 둑 높이만큼 판이 떠 보인다.
    // 수로 바닥에 선 사람의 눈높이에 맞춰야 올려다보지 않는다.
    private const float BoardCenterHeight = 1.35f;

    // 판이 설 자리의 지면이 플레이어 지면보다 이보다 높으면 그 차이를 일부 보정한다.
    // 전부 무시하면 판 아랫부분이 둑에 파묻히기 때문이다.
    private const float BankLiftRatio = 0.5f;

    // 지면을 못 찾았을 때 쓸 값. WaterwayZone 높이 기준.
    private const float GroundFallbackOffset = -0.4f;

    private const float GroundProbeUp = 8f;
    private const float GroundProbeLength = 24f;

    // 기준점보다 이보다 높은 적중은 지면으로 치지 않는다.
    //
    // 1차 배치에서 판이 월드 Y 4.97 에 떠 버린 원인이 이것이었다. 위에서 쏜 레이가
    // 수로 위를 지나는 구조물(Y 약 3.5)에 먼저 맞고, 그걸 지면으로 삼았다.
    // 수로 바닥은 기준점(WaterwayZone, Y 0.5)보다 낮거나 비슷하므로,
    // 기준보다 한참 위에 있는 면은 "머리 위"로 보고 건너뛴다.
    private const float MaxGroundAbove = 1f;

    // ── 적용할 표시 조건 ──────────────────────────────────────────────────
    //
    // 수평 거리 기준이다. 판을 읽으려고 둑 위로 올라서도 높이차가 거리에 더해지지 않는다.
    private const MissionGuideBoard.DistanceMode Mode = MissionGuideBoard.DistanceMode.Horizontal;
    private const float ShowDistance = 6f;
    private const float HideHysteresis = 0.5f;
    private const float FadeDuration = 0.3f;

    // 캔버스 해상도는 그대로 두고 Transform Scale로 1.4배 키운다.
    // 1000 × 1250 캔버스 × 0.00168 = 월드 1.68 × 2.10 m.
    private const float BoardScale = 0.00168f;

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board - Place (Dry Run)", priority = 360)]
    private static void DryRunMenu() => Place(false);

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board - Place", priority = 361)]
    private static void PlaceMenu() => Place(true);

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board - Validate Placement", priority = 362)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 배치 ─────────────────────────────

    private static void Place(bool apply)
    {
        string mode = apply ? "PLACE" : "DRY RUN";
        Debug.Log($"===== Stage2 미션 가이드 보드 배치 [{mode}] =====");

        if (!TryGetScene(out Scene scene))
            return;

        if (!TryGetAnchor(scene, out Transform anchor))
            return;

        if (!TryResolveBoard(scene, apply, out MissionGuideBoard board, out bool created))
            return;

        // ── 위치 계산 ───────────────────────────────────────────
        float radians = PlacementAngleDegrees * Mathf.Deg2Rad;
        var direction = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));

        Vector3 anchorPosition = anchor.position;
        Vector3 flat = anchorPosition + direction * PlacementDistance;

        // 플레이어가 서는 지면(수로 안)과 판이 설 지면(둑)을 따로 잰다.
        float playerGroundY = ProbeGround(anchorPosition, anchorPosition.y, out string playerSource);
        float boardGroundY = ProbeGround(flat, anchorPosition.y, out string boardSource);

        // 기본은 플레이어 눈높이. 판 자리가 더 높으면 그 차이의 절반만 따라 올려,
        // 판이 둑에 파묻히지도 않고 지나치게 떠 보이지도 않게 한다.
        float bankLift = Mathf.Max(0f, boardGroundY - playerGroundY) * BankLiftRatio;
        float centerY = playerGroundY + BoardCenterHeight + bankLift;

        var position = new Vector3(flat.x, centerY, flat.z);

        // 빌보드와 같은 식으로 초기 회전을 잡아 둔다. 수로 쪽에 선 사람을 향한다.
        Vector3 fromAnchor = position - anchorPosition;
        fromAnchor.y = 0f;

        Quaternion rotation = fromAnchor.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(fromAnchor, Vector3.up)
            : Quaternion.identity;

        Debug.Log(
            $"  씬           : {scene.name} ({scene.path})\n" +
            $"  기준(anchor) : {anchor.name}  {Format(anchorPosition)}\n" +
            $"  배치 규칙    : 기준에서 {PlacementAngleDegrees}° 방향 {PlacementDistance} m\n" +
            $"  플레이어 지면: {playerGroundY:F3}  ({playerSource})\n" +
            $"  판 자리 지면 : {boardGroundY:F3}  ({boardSource})\n" +
            $"  높이 계산    : {playerGroundY:F3} + {BoardCenterHeight} " +
            $"+ 둑 보정 {bankLift:F3} = {centerY:F3}\n" +
            $"  → 위치       : {Format(position)}\n" +
            $"  → 회전       : {Format(rotation.eulerAngles)}  (빌보드가 실행 중 덮어쓴다)\n" +
            $"  → 크기       : scale {BoardScale} → 월드 " +
            $"{1000f * BoardScale:F2} × {1250f * BoardScale:F2} m");

        ReportDistances(position, anchorPosition);

        if (!apply)
        {
            Debug.Log(
                "DRY RUN이므로 씬을 바꾸지 않았습니다. 적용하려면 " +
                "'Mission Guide Board - Place' 를 실행하세요.");

            if (created)
                Debug.Log("  (DRY RUN에서는 프리팹을 놓지 않았습니다. 적용 시 새로 놓습니다.)");

            return;
        }

        // ── 적용 ────────────────────────────────────────────────
        Transform boardTransform = board.transform;

        Undo.RecordObject(boardTransform, "Place Mission Guide Board");
        boardTransform.position = position;
        boardTransform.rotation = rotation;
        boardTransform.localScale = Vector3.one * BoardScale;

        if (board.gameObject.name != InstanceName)
        {
            Undo.RecordObject(board.gameObject, "Rename Mission Guide Board");
            board.gameObject.name = InstanceName;
        }

        ApplySettings(board, anchor);
        PreviewInEditor(board);

        EditorUtility.SetDirty(board);
        EditorUtility.SetDirty(boardTransform);
        EditorSceneManager.MarkSceneDirty(scene);

        Selection.activeGameObject = board.gameObject;
        EditorGUIUtility.PingObject(board.gameObject);

        Debug.Log(
            "적용했습니다. 씬은 자동 저장하지 않았습니다 — 확인 후 Ctrl+S 로 저장하세요.\n" +
            "되돌리려면 Ctrl+Z 입니다.");

        Validate();
    }

    /// <summary>표시 조건 값들을 채운다. 참조와 디자인은 건드리지 않는다.</summary>
    private static void ApplySettings(MissionGuideBoard board, Transform anchor)
    {
        var so = new SerializedObject(board);

        SetObject(so, "anchor", anchor);
        SetEnum(so, "characterDisplay", (int)MissionGuideBoard.CharacterDisplay.Both);
        SetEnum(so, "visibleStep", (int)TutorialStep_2.Initial);
        SetEnum(so, "distanceMode", (int)Mode);
        SetBool(so, "hideDuringCutscene", true);
        SetFloat(so, "showDistance", ShowDistance);
        SetFloat(so, "hideHysteresis", HideHysteresis);
        SetFloat(so, "fadeDuration", FadeDuration);
        SetBool(so, "billboard", true);
        SetBool(so, "lockPitch", true);

        so.ApplyModifiedProperties();   // Undo 기록 포함

        Debug.Log(
            "  설정 적용 —\n" +
            $"    anchor             → {anchor.name}\n" +
            "    characterDisplay   → Both\n" +
            "    visibleStep        → Initial\n" +
            $"    distanceMode       → {Mode}\n" +
            "    hideDuringCutscene → true\n" +
            $"    showDistance       → {ShowDistance}\n" +
            $"    hideHysteresis     → {HideHysteresis}\n" +
            $"    fadeDuration       → {FadeDuration}\n" +
            "    billboard          → true\n" +
            "    lockPitch          → true\n" +
            "    (CanvasGroup / TMP / 캐릭터 Sprite / fogDepthPrime 참조는 손대지 않음)");
    }

    /// <summary>
    /// 에디터에서 판이 보이게 CanvasGroup 알파를 1로 올려 둔다.
    /// </summary>
    /// <remarks>
    /// 프리팹은 alpha 0 으로 저장돼 있어 Scene 뷰에서 아무것도 안 보인다. 배치 결과를
    /// 눈으로 확인할 수 없으면 쓸모가 없으므로 여기서 올린다.
    /// 실행 시점에는 <c>MissionGuideBoard.Awake</c> 가 다시 0으로 내리고 조건이 맞을 때
    /// 페이드인하므로, 이 값이 게임 동작에 영향을 주지 않는다.
    /// </remarks>
    private static void PreviewInEditor(MissionGuideBoard board)
    {
        var group = board.GetComponent<CanvasGroup>();

        if (group == null || Mathf.Approximately(group.alpha, 1f))
            return;

        Undo.RecordObject(group, "Preview Mission Guide Board");
        group.alpha = 1f;
        EditorUtility.SetDirty(group);

        Debug.Log("  · Scene 뷰 확인용으로 CanvasGroup.alpha 를 1로 올렸습니다. " +
                  "실행하면 Awake가 0으로 되돌리고 조건에 맞을 때 페이드인합니다.");
    }

    // ───────────────────────────── 대상 찾기 ─────────────────────────────

    private static bool TryGetScene(out Scene result)
    {
        result = default;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.isLoaded || scene.name != SceneName)
                continue;

            result = scene;
            return true;
        }

        Debug.LogError(
            $"'{SceneName}' 씬이 열려 있지 않습니다.\n" +
            "Assets/ReBloom/Scenes/Stage2.unity 를 연 뒤 다시 실행하세요.");
        return false;
    }

    private static bool TryGetAnchor(Scene scene, out Transform anchor)
    {
        anchor = null;

        var found = new List<Transform>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == AnchorName)
                    found.Add(transform);
            }
        }

        if (found.Count == 0)
        {
            Debug.LogError(
                $"'{AnchorName}' 오브젝트를 찾지 못했습니다. 이름이 바뀌었는지 확인하세요.");
            return false;
        }

        if (found.Count > 1)
        {
            Debug.LogWarning(
                $"'{AnchorName}' 이름의 오브젝트가 {found.Count}개입니다. 첫 번째를 씁니다.");
        }

        anchor = found[0];
        return true;
    }

    private static bool TryResolveBoard(
        Scene scene, bool apply, out MissionGuideBoard board, out bool created)
    {
        board = null;
        created = false;

        var found = new List<MissionGuideBoard>();

        foreach (GameObject root in scene.GetRootGameObjects())
            found.AddRange(root.GetComponentsInChildren<MissionGuideBoard>(true));

        if (found.Count > 1)
        {
            Debug.LogError(
                $"씬에 MissionGuideBoard 가 {found.Count}개 있습니다. " +
                "어느 것을 남길지 추측하지 않고 멈춥니다. 하나만 남기고 다시 실행하세요.");

            foreach (MissionGuideBoard duplicate in found)
                Debug.LogError($"  · {GetPath(duplicate.transform)}", duplicate);

            return false;
        }

        if (found.Count == 1)
        {
            board = found[0];
            Debug.Log($"  기존 인스턴스를 씁니다: {GetPath(board.transform)}  (중복 생성 없음)", board);
            return true;
        }

        // 없으면 새로 놓는다.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (prefab == null)
        {
            Debug.LogError($"프리팹이 없습니다: {PrefabPath}");
            return false;
        }

        created = true;

        if (!apply)
        {
            // DRY RUN에서는 씬을 건드리지 않는다. 프리팹 에셋의 컴포넌트로 보고만 한다.
            board = prefab.GetComponent<MissionGuideBoard>();

            if (board == null)
            {
                Debug.LogError($"프리팹에 MissionGuideBoard 가 없습니다: {PrefabPath}");
                return false;
            }

            Debug.Log("  씬에 인스턴스가 없습니다. 적용 시 프리팹을 하나 놓습니다.");
            return true;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

        if (instance == null)
        {
            Debug.LogError($"프리팹을 놓지 못했습니다: {PrefabPath}");
            return false;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create Mission Guide Board");
        board = instance.GetComponent<MissionGuideBoard>();

        if (board == null)
        {
            Debug.LogError($"놓은 오브젝트에 MissionGuideBoard 가 없습니다: {instance.name}");
            return false;
        }

        Debug.Log($"  프리팹을 새로 놓았습니다: {instance.name}", instance);
        return true;
    }

    // ───────────────────────────── 지면 ─────────────────────────────

    /// <summary>
    /// 판을 세울 자리의 지면 높이. 에디터에서도 씬 콜라이더로 레이캐스트가 된다.
    /// </summary>
    /// <remarks>
    /// 트리거는 무시한다 — <c>WaterwayZone</c> 자신이 트리거 구체라 그걸 지면으로 잡으면 안 된다.
    /// 못 맞히면 기준 오브젝트 높이에서 <see cref="GroundFallbackOffset"/> 만큼 내린 값을 쓴다.
    /// </remarks>
    private static float ProbeGround(Vector3 flat, float anchorY, out string source)
    {
        var origin = new Vector3(flat.x, anchorY + GroundProbeUp, flat.z);

        RaycastHit[] hits = Physics.RaycastAll(
            origin, Vector3.down, GroundProbeLength, ~0, QueryTriggerInteraction.Ignore);

        float ceiling = anchorY + MaxGroundAbove;

        bool found = false;
        float best = 0f;
        string bestName = null;
        int skipped = 0;

        foreach (RaycastHit hit in hits)
        {
            // 머리 위 구조물은 지면이 아니다. 지나쳐서 아래를 계속 본다.
            if (hit.point.y > ceiling)
            {
                skipped++;
                continue;
            }

            if (found && hit.point.y <= best)
                continue;

            best = hit.point.y;
            bestName = hit.collider.name;
            found = true;
        }

        string skippedNote = skipped > 0 ? $", 머리 위 {skipped}개 건너뜀" : string.Empty;

        if (found)
        {
            source = $"적중 '{bestName}' @ {best:F3}{skippedNote}";
            return best;
        }

        source = $"적중 없음{skippedNote} → 기준 높이로 대체";
        return anchorY + GroundFallbackOffset;
    }

    private static void ReportDistances(Vector3 position, Vector3 anchorPosition)
    {
        Vector3 flatDelta = position - anchorPosition;
        flatDelta.y = 0f;

        Debug.Log(
            $"  기준까지 수평 거리 {flatDelta.magnitude:F2} m " +
            $"(수로 판정 반경 1.4 m 밖, showDistance {ShowDistance} m 안)\n" +
            "  → 플레이어가 수로에 서 있으면 판이 보이고, 작업 구역과 겹치지 않습니다.");
    }

    // ───────────────────────────── 검증 ─────────────────────────────

    private static void Validate()
    {
        Debug.Log("===== Stage2 미션 가이드 보드 배치 [VALIDATE] =====");

        if (!TryGetScene(out Scene scene))
            return;

        var found = new List<MissionGuideBoard>();

        foreach (GameObject root in scene.GetRootGameObjects())
            found.AddRange(root.GetComponentsInChildren<MissionGuideBoard>(true));

        int problems = 0;

        problems += Expect(found.Count == 1,
            $"씬 내 MissionGuideBoard 1개 (현재 {found.Count}개)", null);

        if (found.Count != 1)
            return;

        MissionGuideBoard board = found[0];
        GameObject go = board.gameObject;

        problems += Expect(go.activeInHierarchy, $"활성 상태 ({GetPath(board.transform)})", go);

        // ── Missing Script ──────────────────────────────────────
        int missingScripts = 0;

        foreach (Transform transform in go.GetComponentsInChildren<Transform>(true))
        {
            Component[] components = transform.GetComponents<Component>();

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    continue;

                Debug.LogError($"  ✗ Missing Script: {GetPath(transform)} [{i}]", transform);
                missingScripts++;
            }
        }

        problems += Expect(missingScripts == 0, $"Missing Script 없음 ({missingScripts}건)", go);

        // ── 표시 조건 ───────────────────────────────────────────
        var so = new SerializedObject(board);

        problems += ExpectRef(so, "anchor", AnchorName);
        problems += ExpectRef(so, "canvasGroup", null);
        problems += ExpectRef(so, "kickerText", null);
        problems += ExpectRef(so, "titleText", null);
        problems += ExpectRef(so, "footerText", null);
        problems += ExpectRef(so, "participationText", null);
        problems += ExpectRef(so, "lumenPortrait", null);
        problems += ExpectRef(so, "echoPortrait", null);
        problems += ExpectRef(so, "lumenNameText", null);
        problems += ExpectRef(so, "echoNameText", null);

        problems += ExpectEnum(so, "characterDisplay", (int)MissionGuideBoard.CharacterDisplay.Both, "Both");
        problems += ExpectEnum(so, "visibleStep", (int)TutorialStep_2.Initial, "Initial");
        problems += ExpectEnum(so, "distanceMode", (int)Mode, Mode.ToString());
        problems += ExpectBool(so, "hideDuringCutscene", true);
        problems += ExpectFloat(so, "showDistance", ShowDistance);
        problems += ExpectFloat(so, "hideHysteresis", HideHysteresis);
        problems += ExpectFloat(so, "fadeDuration", FadeDuration);
        problems += ExpectBool(so, "billboard", true);
        problems += ExpectBool(so, "lockPitch", true);

        SerializedProperty steps = so.FindProperty("steps");

        if (steps != null)
        {
            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty element = steps.GetArrayElementAtIndex(i);
                problems += ExpectRelativeRef(element, "labelText", i);
                problems += ExpectRelativeRef(element, "bodyText", i);
            }
        }

        // ── 캐릭터 Sprite ───────────────────────────────────────
        problems += ExpectSprite(so, "lumenPortrait");
        problems += ExpectSprite(so, "echoPortrait");

        // ── Canvas / XR 안전 ────────────────────────────────────
        var canvas = go.GetComponent<Canvas>();
        problems += Expect(canvas != null, "Canvas 있음", go);

        if (canvas != null)
        {
            problems += Expect(canvas.renderMode == RenderMode.WorldSpace,
                $"Canvas.renderMode = WorldSpace (현재 {canvas.renderMode})", go);
        }

        problems += Expect(go.GetComponentInChildren<GraphicRaycaster>(true) == null,
            "GraphicRaycaster 없음", go);
        problems += Expect(go.GetComponentInChildren<Collider>(true) == null,
            "Collider 없음", go);

        foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>(true))
        {
            if (!graphic.raycastTarget)
                continue;

            Debug.LogError($"  ✗ raycastTarget이 켜져 있습니다: {graphic.name}", graphic);
            problems++;
        }

        // ── 폰트 ────────────────────────────────────────────────
        foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font != null)
                continue;

            Debug.LogError($"  ✗ {text.name}: Font Asset이 비어 있습니다.", text);
            problems++;
        }

        // ── 위치 ────────────────────────────────────────────────
        SerializedProperty anchorProperty = so.FindProperty("anchor");
        var anchorTransform = anchorProperty != null
            ? anchorProperty.objectReferenceValue as Transform
            : null;

        if (anchorTransform != null)
        {
            Vector3 delta = board.transform.position - anchorTransform.position;
            float heightDelta = delta.y;
            delta.y = 0f;

            var rect = board.transform as RectTransform;
            Vector2 world = rect != null
                ? rect.sizeDelta * board.transform.localScale.x
                : Vector2.zero;

            Debug.Log(
                $"  · 위치 {Format(board.transform.position)} / " +
                $"회전 {Format(board.transform.eulerAngles)} / " +
                $"scale {board.transform.localScale.x}\n" +
                $"  · 월드 크기 {world.x:F2} × {world.y:F2} m\n" +
                $"  · 기준까지 수평 {delta.magnitude:F2} m / 높이차 {heightDelta:F2} m");

            problems += Expect(delta.magnitude > 1.4f,
                $"수로 판정 반경(1.4 m) 밖 (현재 {delta.magnitude:F2} m)", go);
            problems += Expect(delta.magnitude < ShowDistance,
                $"showDistance({ShowDistance} m) 안 (현재 {delta.magnitude:F2} m)", go);
            problems += Expect(Mathf.Approximately(board.transform.localScale.x, BoardScale),
                $"Scale {BoardScale} (현재 {board.transform.localScale.x})", go);

            // 1차 배치에서 머리 위 구조물을 지면으로 잡아 판이 4.5 m 떠올랐던 적이 있다.
            // 눈높이 배치라면 기준점보다 0.5~2.5 m 위가 정상이다.
            problems += Expect(heightDelta > 0.3f && heightDelta < 2.8f,
                $"기준점 대비 높이 0.3~2.8 m (현재 {heightDelta:F2} m)", go);
        }

        // ── 깊이 프라임 (안개 대응) ─────────────────────────────
        SerializedProperty depthPrime = so.FindProperty("fogDepthPrime");

        if (depthPrime == null || depthPrime.objectReferenceValue == null)
        {
            Debug.LogWarning(
                "  ! fogDepthPrime 이 비어 있습니다. 보드는 동작하지만 화면 안개(FlatKitFog, " +
                "renderEvent 550)가 보드를 배경 깊이로 덮어 더 흐리게 보입니다. " +
                "'Mission Guide Board Prefab - Restyle' 로 프리팹을 갱신하세요.");
        }
        else
        {
            Debug.Log($"  ✓ fogDepthPrime → {depthPrime.objectReferenceValue.name}");
        }

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음. (실행 중 동작은 Play로 확인하세요)"
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    // ───────────────────────────── 보조 ─────────────────────────────

    private static string GetPath(Transform transform)
    {
        string path = transform.name;

        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;

        return path;
    }

    private static string Format(Vector3 value)
        => $"({value.x:F3}, {value.y:F3}, {value.z:F3})";

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

    private static int ExpectRef(SerializedObject so, string fieldName, string expectedName)
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

        string actual = property.objectReferenceValue.name;

        if (expectedName != null && actual != expectedName)
        {
            Debug.LogError($"  ✗ {fieldName} → {actual} (기대: {expectedName})");
            return 1;
        }

        Debug.Log($"  ✓ {fieldName} → {actual}");
        return 0;
    }

    private static int ExpectRelativeRef(SerializedProperty element, string fieldName, int index)
    {
        SerializedProperty property = element.FindPropertyRelative(fieldName);

        if (property == null || property.objectReferenceValue == null)
        {
            Debug.LogError($"  ✗ steps[{index}].{fieldName} 참조가 비어 있습니다.");
            return 1;
        }

        Debug.Log($"  ✓ steps[{index}].{fieldName} → {property.objectReferenceValue.name}");
        return 0;
    }

    private static int ExpectSprite(SerializedObject so, string fieldName)
    {
        SerializedProperty property = so.FindProperty(fieldName);
        var image = property != null ? property.objectReferenceValue as Image : null;

        if (image == null)
            return 0;   // 참조 누락은 ExpectRef가 이미 셌다

        if (image.sprite == null)
        {
            Debug.LogError($"  ✗ {fieldName}.sprite 가 비어 있습니다.", image);
            return 1;
        }

        Debug.Log($"  ✓ {fieldName}.sprite → {image.sprite.name}", image);
        return 0;
    }

    private static int ExpectEnum(SerializedObject so, string fieldName, int expected, string label)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ '{fieldName}' 필드가 없습니다.");
            return 1;
        }

        if (property.enumValueIndex != expected)
        {
            Debug.LogError($"  ✗ {fieldName} = {property.enumValueIndex} (기대: {label})");
            return 1;
        }

        Debug.Log($"  ✓ {fieldName} = {label}");
        return 0;
    }

    private static int ExpectBool(SerializedObject so, string fieldName, bool expected)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ '{fieldName}' 필드가 없습니다.");
            return 1;
        }

        if (property.boolValue != expected)
        {
            Debug.LogError($"  ✗ {fieldName} = {property.boolValue} (기대: {expected})");
            return 1;
        }

        Debug.Log($"  ✓ {fieldName} = {expected}");
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

        if (!Mathf.Approximately(property.floatValue, expected))
        {
            Debug.LogError($"  ✗ {fieldName} = {property.floatValue} (기대: {expected})");
            return 1;
        }

        Debug.Log($"  ✓ {fieldName} = {expected}");
        return 0;
    }

    private static void SetObject(SerializedObject so, string fieldName, Object value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static void SetEnum(SerializedObject so, string fieldName, int value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.enumValueIndex = value;
    }

    private static void SetBool(SerializedObject so, string fieldName, bool value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.boolValue = value;
    }

    private static void SetFloat(SerializedObject so, string fieldName, float value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.floatValue = value;
    }
}
