using System.Collections.Generic;
using System.Linq;
using ReBloom.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Stage 3 씬에서 <see cref="SolarPanelRepairCutscene"/>의 Panels 배열을 자동 구성한다.
///
/// 메뉴: Re_Bloom / Stage3 / Solar Repair Cutscene Setup
///  - Dry Run : 무엇이 바뀔지 콘솔에 출력만 한다.
///  - Apply   : 실제로 Shot Point를 만들고 배열을 채운다. 씬 저장도 한다.
///
/// 안전 규칙:
///  - 이미 배열에 들어 있는 패널(reflector fileID가 같은 것)은 건드리지 않는다.
///  - 이미 존재하는 Shot Point의 Transform 값은 덮어쓰지 않는다.
///  - 반복 실행해도 중복 생성되지 않는다.
/// </summary>
public static class SolarPanelRepairCutsceneSetup
{
    private const string ManagerName = "SolarPanelRepairCutsceneManager";
    private const string ShotPointPrefix = "ShotPoint_";
    private const float ShotDistance = 2.5f;
    private const float ShotHeight = 0.3f;

    [MenuItem("Re_Bloom/Stage3/Solar Repair Cutscene Setup - Dry Run")]
    private static void DryRun()
    {
        Run(false);
    }

    [MenuItem("Re_Bloom/Stage3/Solar Repair Cutscene Setup - Apply")]
    private static void Apply()
    {
        Run(true);
    }

    [MenuItem("Re_Bloom/Stage3/Update Repair Cutscene Timing")]
    private static void UpdateRepairCutsceneTiming()
    {
        SolarPanelRepairCutscene manager = Object.FindFirstObjectByType<SolarPanelRepairCutscene>();
        if (manager == null)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] 씬에서 SolarPanelRepairCutscene을 찾지 못했습니다.");
            return;
        }

        SerializedObject so = new SerializedObject(manager);

        so.FindProperty("fadeDuration").floatValue = 0.5f;
        so.FindProperty("preGlowDelay").floatValue = 0.6f;
        so.FindProperty("glowDuration").floatValue = 1.8f;
        so.FindProperty("postGlowHold").floatValue = 1.2f;
        so.FindProperty("glowColor").colorValue = new Color(0f, 2f, 1.6f, 1f);
        so.FindProperty("repairVolume").floatValue = 0.7f;

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        Scene scene = manager.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[SolarRepairCutsceneSetup] 타이밍 및 발광 설정 업데이트 완료:\n" +
                  "  fadeDuration=0.5, preGlowDelay=0.6, glowDuration=1.8, postGlowHold=1.2\n" +
                  "  glowColor=(0, 2, 1.6) HDR, repairVolume=0.7",
                  manager);
    }

    [MenuItem("Re_Bloom/Stage3/Link Repair AudioClip (alive_decal)")]
    private static void LinkRepairAudioClip()
    {
        SolarPanelRepairCutscene manager = Object.FindFirstObjectByType<SolarPanelRepairCutscene>();
        if (manager == null)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] 씬에서 SolarPanelRepairCutscene을 찾지 못했습니다.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("alive_decal t:AudioClip");
        if (guids.Length == 0)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] alive_decal AudioClip을 프로젝트에서 찾지 못했습니다.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);

        SerializedObject so = new SerializedObject(manager);
        so.FindProperty("repairClip").objectReferenceValue = clip;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        Scene scene = manager.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[SolarRepairCutsceneSetup] repairClip 연결 완료: {path}", manager);
    }

    [MenuItem("Re_Bloom/Stage3/Link Repair Marker Sprite (RootFound_Check)")]
    private static void LinkRepairMarkerSprite()
    {
        SolarPanelRepairCutscene manager = Object.FindFirstObjectByType<SolarPanelRepairCutscene>();
        if (manager == null)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] 씬에서 SolarPanelRepairCutscene을 찾지 못했습니다.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("RootFound_Check t:Sprite");
        if (guids.Length == 0)
            guids = AssetDatabase.FindAssets("RootFound_Check t:Texture2D");

        if (guids.Length == 0)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] RootFound_Check 스프라이트를 프로젝트에서 찾지 못했습니다.");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (sprite == null)
        {
            Debug.LogError($"[SolarRepairCutsceneSetup] {path}에서 Sprite를 로드하지 못했습니다.");
            return;
        }

        SerializedObject so = new SerializedObject(manager);
        so.FindProperty("repairMarkerSprite").objectReferenceValue = sprite;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        Scene scene = manager.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[SolarRepairCutsceneSetup] repairMarkerSprite 연결 완료: {path}", manager);
    }

    [MenuItem("Re_Bloom/Stage3/Debug Panel Outline Bounds")]
    private static void DebugPanelOutlineBounds()
    {
        SolarPanelCleanView[] views = Object.FindObjectsByType<SolarPanelCleanView>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (views.Length == 0)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] 씬에서 SolarPanelCleanView를 찾지 못했습니다.");
            return;
        }

        foreach (SolarPanelCleanView cv in views)
        {
            Transform root = cv.transform;
            Material broken = cv.brokenMaterial;
            Material clean = cv.cleanMaterial;

            if (broken == null && clean == null)
            {
                Debug.LogWarning($"  {cv.name}: 머티리얼 미연결", cv);
                continue;
            }

            SolarReflector reflector = cv.GetComponentInChildren<SolarReflector>();
            Transform frameParent = reflector != null ? reflector.transform : root;
            string normalInfo = reflector != null
                ? $"SolarReflector.up={reflector.transform.up}"
                : "(SolarReflector 없음)";

            Bounds combinedLocal = default;
            bool first = true;
            int includedCount = 0;
            int excludedCount = 0;

            foreach (Renderer r in cv.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer) continue;

                bool match = false;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == broken || mats[i] == clean)
                    { match = true; break; }
                }
                if (!match) continue;

                Bounds lb = r.localBounds;
                Vector3 ext = lb.extents;

                float thickness = 0f;
                bool included = true;

                if (reflector != null)
                {
                    Vector3 localDir = r.transform.InverseTransformDirection(
                        reflector.transform.up).normalized;
                    float halfThick = Mathf.Abs(ext.x * localDir.x)
                                    + Mathf.Abs(ext.y * localDir.y)
                                    + Mathf.Abs(ext.z * localDir.z);
                    thickness = halfThick * 2f;
                    included = thickness <= 1.0f;
                }

                Bounds parentBounds = default;
                bool pbFirst = true;

                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 lc = lb.center + new Vector3(ext.x * x, ext.y * y, ext.z * z);
                    Vector3 wc = r.transform.TransformPoint(lc);
                    Vector3 pc = frameParent.InverseTransformPoint(wc);
                    if (pbFirst) { parentBounds = new Bounds(pc, Vector3.zero); pbFirst = false; }
                    else parentBounds.Encapsulate(pc);
                }

                Debug.Log($"  {cv.name}/{r.name}: localBounds center={lb.center} size={lb.size}" +
                          $" → normalThickness={thickness:F3}" +
                          $" {(included ? "포함" : "제외(지지대)")}", r);

                if (!included) { excludedCount++; continue; }

                includedCount++;
                if (first) { combinedLocal = parentBounds; first = false; }
                else { combinedLocal.Encapsulate(parentBounds.min); combinedLocal.Encapsulate(parentBounds.max); }
            }

            if (first)
            {
                Debug.LogWarning($"  {cv.name}: 판 본체 렌더러 없음", cv);
                continue;
            }

            string refSpace = reflector != null ? "reflector-local" : "root";

            Debug.Log(
                $"[SolarRepairCutsceneSetup] {cv.name}:\n" +
                $"  포함 렌더러: {includedCount}개, 제외(지지대): {excludedCount}개\n" +
                $"  filtered bounds ({refSpace}): center={combinedLocal.center} size={combinedLocal.size}\n" +
                $"  normal: {normalInfo}\n" +
                $"  frame surface: Y={combinedLocal.max[1]:F3}+offset",
                cv);
        }
    }

    [MenuItem("Re_Bloom/Stage3/Align Panel B Shot Point")]
    private static void AlignPanelBShotPoint()
    {
        SolarPanelRepairCutscene manager = Object.FindFirstObjectByType<SolarPanelRepairCutscene>();
        if (manager == null)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] 씬에서 SolarPanelRepairCutscene을 찾지 못했습니다.");
            return;
        }

        SerializedObject so = new SerializedObject(manager);
        SerializedProperty panelsProp = so.FindProperty("panels");

        if (panelsProp.arraySize == 0)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] Panels 배열이 비어 있습니다.");
            return;
        }

        SerializedProperty entry = panelsProp.GetArrayElementAtIndex(0);
        SolarReflector reflector =
            entry.FindPropertyRelative("reflector").objectReferenceValue as SolarReflector;

        if (reflector == null)
        {
            Debug.LogError("[SolarRepairCutsceneSetup] Panels[0]에 reflector가 없습니다.");
            return;
        }

        SerializedProperty shotPointProp = entry.FindPropertyRelative("shotPoint");
        Transform shotPoint = shotPointProp.objectReferenceValue as Transform;

        if (shotPoint == null)
        {
            shotPoint = FindExistingShotPoint(reflector, manager.transform);
            if (shotPoint != null)
            {
                shotPointProp.objectReferenceValue = shotPoint;
                so.ApplyModifiedProperties();
                Debug.Log($"[SolarRepairCutsceneSetup] 기존 Shot Point를 연결했습니다: {shotPoint.name}", shotPoint);
            }
            else
            {
                Debug.LogError($"[SolarRepairCutsceneSetup] Panels[0] ({reflector.name})에 " +
                               "Shot Point가 없습니다. Setup - Apply를 먼저 실행하거나 수동으로 연결해 주세요.");
                return;
            }
        }

        CalculateShotTransform(reflector, out Vector3 position, out Quaternion rotation);

        Undo.RecordObject(shotPoint, "Align Panel B Shot Point");
        shotPoint.SetPositionAndRotation(position, rotation);
        EditorUtility.SetDirty(shotPoint);

        Scene scene = manager.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[SolarRepairCutsceneSetup] Panel B Shot Point 재정렬 완료: {shotPoint.name}\n" +
                  $"  위치=({position.x:F1}, {position.y:F1}, {position.z:F1})\n" +
                  $"  reflector={reflector.name}", shotPoint);
    }

    private static void Run(bool apply)
    {
        string tag = apply ? "Apply" : "Dry Run";
        Debug.Log($"[SolarRepairCutsceneSetup] === {tag} 시작 ===");

        // 1. 매니저 찾기
        SolarPanelRepairCutscene manager = Object.FindFirstObjectByType<SolarPanelRepairCutscene>();
        if (manager == null)
        {
            Debug.LogError($"[SolarRepairCutsceneSetup] 씬에서 {ManagerName}을 찾지 못했습니다. " +
                           "SolarPanelRepairCutscene 컴포넌트가 붙은 GameObject를 만들어 주세요.");
            return;
        }

        Debug.Log($"[SolarRepairCutsceneSetup] 매니저: {manager.gameObject.name}", manager);

        // 2. 현재 panels 배열 읽기
        SerializedObject so = new SerializedObject(manager);
        SerializedProperty panelsProp = so.FindProperty("panels");

        // 이미 연결된 reflector의 instanceID 수집
        HashSet<int> existingReflectorIds = new HashSet<int>();
        for (int i = 0; i < panelsProp.arraySize; i++)
        {
            SerializedProperty entry = panelsProp.GetArrayElementAtIndex(i);
            Object refObj = entry.FindPropertyRelative("reflector").objectReferenceValue;
            if (refObj != null)
                existingReflectorIds.Add(refObj.GetInstanceID());
        }

        Debug.Log($"[SolarRepairCutsceneSetup] 기존 배열 항목 수: {panelsProp.arraySize}, " +
                  $"연결된 reflector 수: {existingReflectorIds.Count}");

        // 3. 씬의 모든 고장 패널 찾기
        SolarReflector[] allReflectors = Object.FindObjectsByType<SolarReflector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        List<SolarReflector> brokenPanels = allReflectors
            .Where(r => r.broken)
            .OrderBy(r => r.transform.root.name)
            .ThenBy(r => r.name)
            .ToList();

        Debug.Log($"[SolarRepairCutsceneSetup] 씬의 전체 SolarReflector: {allReflectors.Length}개, " +
                  $"고장(broken=true): {brokenPanels.Count}개");

        foreach (SolarReflector panel in brokenPanels)
        {
            string status = existingReflectorIds.Contains(panel.GetInstanceID()) ? "기존" : "신규";
            Debug.Log($"  [{status}] {GetHierarchyPath(panel.gameObject)}", panel);
        }

        // 4. 신규 패널 목록
        List<SolarReflector> newPanels = brokenPanels
            .Where(r => !existingReflectorIds.Contains(r.GetInstanceID()))
            .ToList();

        if (newPanels.Count == 0)
        {
            Debug.Log($"[SolarRepairCutsceneSetup] 추가할 패널이 없습니다. 모든 고장 패널이 이미 등록돼 있습니다.");

            // 기존 항목 중 shotPoint가 비어 있는 것 보완
            if (apply)
                EnsureExistingShotPoints(so, panelsProp, manager.transform);

            return;
        }

        Debug.Log($"[SolarRepairCutsceneSetup] 새로 추가할 패널: {newPanels.Count}개");

        if (!apply)
        {
            Debug.Log($"[SolarRepairCutsceneSetup] === Dry Run 종료. Apply로 실행하면 위 패널들이 추가됩니다. ===");
            return;
        }

        // 5. 기존 항목의 빈 shotPoint 보완
        EnsureExistingShotPoints(so, panelsProp, manager.transform);

        // 6. 신규 패널 추가
        foreach (SolarReflector reflector in newPanels)
        {
            SolarPanelCleanView cleanView = reflector.GetComponentInParent<SolarPanelCleanView>();
            if (cleanView == null)
                cleanView = reflector.transform.root.GetComponentInChildren<SolarPanelCleanView>(true);

            Transform shotPoint = CreateShotPoint(reflector, manager.transform);

            int index = panelsProp.arraySize;
            panelsProp.InsertArrayElementAtIndex(index);
            SerializedProperty entry = panelsProp.GetArrayElementAtIndex(index);

            entry.FindPropertyRelative("reflector").objectReferenceValue = reflector;
            entry.FindPropertyRelative("cleanView").objectReferenceValue = cleanView;
            entry.FindPropertyRelative("shotPoint").objectReferenceValue = shotPoint;

            string cvName = cleanView != null ? cleanView.gameObject.name : "(없음)";
            Debug.Log($"  추가: {reflector.name} → cleanView={cvName}, shotPoint={shotPoint.name}", reflector);
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        // 7. 씬 저장
        Scene scene = manager.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[SolarRepairCutsceneSetup] === Apply 완료. " +
                  $"패널 {panelsProp.arraySize}개 등록, 씬 저장됨. ===");
    }

    /// <summary>기존 항목 중 shotPoint가 비어 있는 것에 Shot Point를 만들어 넣는다.</summary>
    private static void EnsureExistingShotPoints(
        SerializedObject so, SerializedProperty panelsProp, Transform managerTransform)
    {
        for (int i = 0; i < panelsProp.arraySize; i++)
        {
            SerializedProperty entry = panelsProp.GetArrayElementAtIndex(i);
            SerializedProperty shotPointProp = entry.FindPropertyRelative("shotPoint");

            if (shotPointProp.objectReferenceValue != null) continue;

            SolarReflector reflector =
                entry.FindPropertyRelative("reflector").objectReferenceValue as SolarReflector;

            if (reflector == null) continue;

            Transform shotPoint = FindExistingShotPoint(reflector, managerTransform);
            if (shotPoint == null)
                shotPoint = CreateShotPoint(reflector, managerTransform);

            shotPointProp.objectReferenceValue = shotPoint;
            Debug.Log($"  기존 항목 [{i}] {reflector.name}에 shotPoint 연결: {shotPoint.name}", reflector);
        }

        so.ApplyModifiedProperties();
    }

    /// <summary>이미 만들어진 Shot Point가 있는지 이름으로 찾는다.</summary>
    private static Transform FindExistingShotPoint(SolarReflector reflector, Transform parent)
    {
        string expectedName = ShotPointPrefix + GetPanelLabel(reflector);

        foreach (Transform child in parent)
        {
            if (child.name == expectedName)
                return child;
        }

        return null;
    }

    /// <summary>
    /// 패널 법선으로부터 Shot Point 위치·회전을 계산한다.
    /// </summary>
    private static void CalculateShotTransform(
        SolarReflector reflector, out Vector3 position, out Quaternion rotation)
    {
        Vector3 panelPosition = reflector.transform.position;
        Vector3 panelNormal = reflector.Normal;

        Vector3 horizontalNormal = Vector3.ProjectOnPlane(panelNormal, Vector3.up);
        if (horizontalNormal.sqrMagnitude < 0.01f)
            horizontalNormal = Vector3.ProjectOnPlane(reflector.transform.root.forward, Vector3.up);
        horizontalNormal = horizontalNormal.normalized;

        position = panelPosition + horizontalNormal * ShotDistance + Vector3.up * ShotHeight;

        Vector3 lookDir = (panelPosition - position);
        lookDir.y = 0f;
        rotation = lookDir.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
            : Quaternion.identity;

        float pitchAngle = Mathf.Atan2(ShotHeight, ShotDistance) * Mathf.Rad2Deg;
        rotation *= Quaternion.Euler(pitchAngle, 0f, 0f);
    }

    /// <summary>
    /// 패널 앞에 촬영 위치를 만든다.
    /// 판 법선 방향으로 <see cref="ShotDistance"/>m 떨어지고, 약간 위에서 내려다본다.
    /// </summary>
    private static Transform CreateShotPoint(SolarReflector reflector, Transform parent)
    {
        string label = GetPanelLabel(reflector);
        string shotPointName = ShotPointPrefix + label;

        // 이미 있으면 재활용 (Transform 값은 건드리지 않는다)
        Transform existing = FindExistingShotPoint(reflector, parent);
        if (existing != null)
        {
            Debug.Log($"  Shot Point 재활용: {existing.name}", existing);
            return existing;
        }

        CalculateShotTransform(reflector, out Vector3 shotPosition, out Quaternion shotRotation);

        GameObject go = new GameObject(shotPointName);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(shotPosition, shotRotation);

        Undo.RegisterCreatedObjectUndo(go, "Create Solar Repair Shot Point");
        Debug.Log($"  Shot Point 생성: {shotPointName} " +
                  $"위치=({shotPosition.x:F1}, {shotPosition.y:F1}, {shotPosition.z:F1})", go);

        return go.transform;
    }

    /// <summary>
    /// 패널을 구별하는 짧은 고유 이름.
    /// 프리팹 인스턴스의 루트 이름이나 SolarPanelCleanView가 붙은 오브젝트 이름에서 뽑는다.
    /// 같은 이름이 겹치지 않도록 instanceID 꼬리를 붙인다.
    /// </summary>
    private static string GetPanelLabel(SolarReflector reflector)
    {
        // SolarPanelCleanView는 프리팹 루트에 있다 — 그 오브젝트 이름이 가장 의미 있다.
        SolarPanelCleanView cv = reflector.GetComponentInParent<SolarPanelCleanView>();
        string baseName = cv != null ? cv.gameObject.name : reflector.transform.root.name;

        // 공백·괄호를 정리해서 짧은 식별자로
        baseName = baseName.Replace(" ", "").Replace("(", "_").Replace(")", "");

        // instanceID로 고유성 보장 (음수이므로 절대값)
        int id = Mathf.Abs(reflector.GetInstanceID());
        return $"{baseName}_{id}";
    }

    private static string GetHierarchyPath(GameObject go)
    {
        string path = go.name;
        Transform parent = go.transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }
        return path;
    }
}
