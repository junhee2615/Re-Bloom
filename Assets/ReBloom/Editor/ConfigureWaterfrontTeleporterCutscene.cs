#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ConfigureWaterfrontTeleporterCutscene
{
    private const string ScenePath =
        "Assets/ReBloom/Scenes/Stage3.unity";
    private const string ManagerName =
        "WaterfrontTeleporterCutsceneManager";
    private const string TeleporterName =
        "WaterfrontToFinalTeleporter";
    private const string StoolName = "Stool";
    private const string ClipPath =
        "Assets/ReBloom/Sounds/Stage2/Stage2_TeleporterActivate.mp3";

    private const float HeadHeight = 1.6f;
    private const float HorizontalDistance = 30f;

    [MenuItem("ReBloom/Setup/Configure Waterfront Teleporter Cutscene")]
    public static void Configure()
    {
        Scene scene = EnsureScene();
        if (!scene.IsValid())
            return;

        // ── 텔레포터 검색 ──

        Transform teleporter = FindTransformByName(TeleporterName);
        if (teleporter == null)
        {
            Debug.LogError(
                $"[WaterfrontTeleporterSetup] {TeleporterName}을 찾지 못했습니다. " +
                "Stage3.unity 씬에 Stage3_Environment 프리팹이 배치되어 있는지 확인하세요.");
            return;
        }

        Vector3 teleporterPos = teleporter.position;

        // ── Stool 발판 검색 (Mission 2 협동 미션 발판) ──

        Transform[] stools = FindAllTransformsByName(StoolName);
        if (stools.Length < 2)
        {
            Debug.LogError(
                $"[WaterfrontTeleporterSetup] '{StoolName}' 오브젝트를 2개 이상 찾지 못했습니다. " +
                $"(발견: {stools.Length}개) Stage3_Environment 프리팹을 확인하세요.");
            return;
        }

        Transform stoolA = stools[0];
        Transform stoolB = stools[1];

        Debug.Log(
            $"[WaterfrontTeleporterSetup] Stool 발견:\n" +
            $"  Stool A: {GetHierarchyPath(stoolA)} → pos={stoolA.position}\n" +
            $"  Stool B: {GetHierarchyPath(stoolB)} → pos={stoolB.position}");

        // ── Stool 표면 높이 계산 ──

        float surfaceYA = ComputeMeshSurfaceY(stoolA);
        float surfaceYB = ComputeMeshSurfaceY(stoolB);
        float avgSurfaceY = (surfaceYA + surfaceYB) * 0.5f;

        Vector3 stoolMid = (stoolA.position + stoolB.position) * 0.5f;

        Debug.Log(
            $"[WaterfrontTeleporterSetup] Stool 분석:\n" +
            $"  Stool A 표면 Y: {surfaceYA:F3} (pivot={stoolA.position.y:F3}, scaleY={stoolA.lossyScale.y:F3})\n" +
            $"  Stool B 표면 Y: {surfaceYB:F3} (pivot={stoolB.position.y:F3}, scaleY={stoolB.lossyScale.y:F3})\n" +
            $"  중간점: {stoolMid}\n" +
            $"  평균 표면 Y: {avgSurfaceY:F3}");

        // ── 카메라 경로 계산 (3-Waypoint) ──

        float headY = avgSurfaceY + HeadHeight;
        float riseY = 15f;

        Vector3 toTeleporter = teleporterPos - stoolMid;
        toTeleporter.y = 0f;
        Vector3 approachDir = toTeleporter.sqrMagnitude > 0.01f
            ? toTeleporter.normalized
            : Vector3.forward;

        // StartShot: 두 Stool 중간, HMD 높이
        Vector3 startPos = new Vector3(stoolMid.x, headY, stoolMid.z);

        // RisePoint: StartShot 바로 위, Y=15 (수직 상승)
        Vector3 risePos = new Vector3(stoolMid.x, riseY, stoolMid.z);

        // EndShot: RisePoint에서 텔레포터 방향으로 30m 수평 이동, Y=15 유지
        Vector3 endPos = risePos + approachDir * HorizontalDistance;

        // ── 회전: 각 위치에서 텔레포터 방향 ──

        Quaternion startRot = LookAtTarget(startPos, teleporterPos + Vector3.up * 5f);
        Quaternion riseRot = LookAtTarget(risePos, teleporterPos + Vector3.up * 2f);
        Quaternion endRot = LookAtTarget(endPos, teleporterPos);

        // ── 경로 장애물 검사 ──

        CheckPathObstacle("StartShot→RisePoint", startPos, risePos);
        CheckPathObstacle("RisePoint→EndShot", risePos, endPos);

        // ── 매니저 GameObject 생성/갱신 ──

        WaterfrontTeleporterCutscene manager =
            FindObjectByName<WaterfrontTeleporterCutscene>(ManagerName);

        GameObject managerGO;
        if (manager != null)
        {
            managerGO = manager.gameObject;
            Debug.Log(
                $"[WaterfrontTeleporterSetup] 기존 {ManagerName}을 갱신합니다.", managerGO);
        }
        else
        {
            managerGO = new GameObject(ManagerName);
            managerGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            manager = managerGO.AddComponent<WaterfrontTeleporterCutscene>();
            Undo.RegisterCreatedObjectUndo(managerGO, "Create Waterfront Teleporter Cutscene");
        }

        // ── 웨이포인트 생성/갱신 (3개) ──

        Transform startShotT =
            FindOrCreateChild(managerGO.transform, "WaterfrontTeleporterStartShot");
        startShotT.SetPositionAndRotation(startPos, startRot);

        Transform risePointT =
            FindOrCreateChild(managerGO.transform, "WaterfrontTeleporterRisePoint");
        risePointT.SetPositionAndRotation(risePos, riseRot);

        Transform endShotT =
            FindOrCreateChild(managerGO.transform, "WaterfrontTeleporterEndShot");
        endShotT.SetPositionAndRotation(endPos, endRot);

        // 이전 버전의 MidPoint가 남아있으면 제거
        Transform oldMidPoint = managerGO.transform.Find("WaterfrontTeleporterMidPoint");
        if (oldMidPoint != null)
        {
            Undo.DestroyObjectImmediate(oldMidPoint.gameObject);
            Debug.Log("[WaterfrontTeleporterSetup] 이전 MidPoint를 제거했습니다.");
        }

        // ── VFX 연결 ──

        GameObject fireCircle = FindChildGameObject(teleporter, "Fire Circle");
        GameObject splash = FindChildGameObject(teleporter, "Splash");

        if (fireCircle == null || splash == null)
        {
            Debug.LogWarning(
                $"[WaterfrontTeleporterSetup] {TeleporterName}에서 " +
                "Fire Circle 또는 Splash를 찾지 못했습니다. VFX 참조를 수동으로 설정하세요.");
        }
        else
        {
            fireCircle.SetActive(false);
            splash.SetActive(false);
        }

        // ── AudioClip ──

        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        if (clip == null)
        {
            Debug.LogWarning(
                $"[WaterfrontTeleporterSetup] {ClipPath}를 찾지 못했습니다.");
        }

        // ── 컴포넌트 프로퍼티 설정 ──

        SerializedObject so = new SerializedObject(manager);

        SetObjectField(so, "startShot", startShotT);
        SetObjectField(so, "risePoint", risePointT);
        SetObjectField(so, "endShot", endShotT);

        SetFloatField(so, "fadeDuration", 0.6f);
        SetFloatField(so, "riseDuration", 2.5f);
        SetFloatField(so, "forwardDuration", 4f);
        SetFloatField(so, "holdDuration", 2.5f);

        SetFloatField(so, "cornerBlendDistance", 3f);

        SerializedProperty curveProp = so.FindProperty("movementEaseCurve");
        if (curveProp != null)
            curveProp.animationCurveValue = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        SetObjectField(so, "teleporterActivateClip", clip);
        SetFloatField(so, "teleporterActivateVolume", 0.4f);

        SetBoolField(so, "smoothCameraMovement", true);
        SetBoolField(so, "followYaw", true);
        SetBoolField(so, "followPitch", false);

        SetBoolField(so, "disableResonance", true);
        SetBoolField(so, "disableBuiltinFog", true);
        SetBoolField(so, "hideRemoteAvatars", true);
        SetBoolField(so, "lockLocomotion", true);
        SetBoolField(so, "hideTutorialCanvas", true);

        SerializedProperty vfx = so.FindProperty("teleporterVfxObjects");
        if (fireCircle != null && splash != null)
        {
            vfx.arraySize = 2;
            vfx.GetArrayElementAtIndex(0).objectReferenceValue = fireCircle;
            vfx.GetArrayElementAtIndex(1).objectReferenceValue = splash;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        // ── 수변 복원 컷씬 연결 ──

        Stage3RiverCutscene riverCutscene = null;
        Stage3RiverCutscene[] rivers = Object.FindObjectsByType<Stage3RiverCutscene>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (rivers.Length > 0)
            riverCutscene = rivers[0];

        if (riverCutscene != null)
        {
            SerializedObject riverSO = new SerializedObject(riverCutscene);
            SetObjectField(riverSO, "teleporterCutscene", manager);
            riverSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(riverCutscene);
            Debug.Log(
                $"[WaterfrontTeleporterSetup] Stage3RiverCutscene에 텔레포터 컷씬 참조를 연결했습니다.",
                riverCutscene);
        }
        else
        {
            Debug.LogWarning(
                "[WaterfrontTeleporterSetup] Stage3RiverCutscene을 찾지 못했습니다. " +
                "수변 복원 컷씬과 텔레포터 컷씬의 연결을 수동으로 설정해야 합니다.");
        }

        // ── 텔레포터 잠금 연결 ──

        ZoneTeleporter zoneTeleporter = teleporter.GetComponent<ZoneTeleporter>();
        if (zoneTeleporter != null)
        {
            SerializedObject ztSO = new SerializedObject(zoneTeleporter);
            SetObjectField(ztSO, "requiredWaterfrontCutscene", manager);
            ztSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(zoneTeleporter);
            Debug.Log(
                $"[WaterfrontTeleporterSetup] {TeleporterName}의 ZoneTeleporter에 " +
                "컷씬 완료 잠금을 연결했습니다.",
                zoneTeleporter);
        }
        else
        {
            Debug.LogWarning(
                $"[WaterfrontTeleporterSetup] {TeleporterName}에서 ZoneTeleporter 컴포넌트를 " +
                "찾지 못했습니다. 텔레포터 잠금을 수동으로 설정해야 합니다.");
        }

        EditorSceneManager.MarkSceneDirty(scene);

        // ── 결과 출력 ──

        float riseHeight = riseY - headY;
        float startToRise = Vector3.Distance(startPos, risePos);
        float riseToEnd = Vector3.Distance(risePos, endPos);
        float totalMove = startToRise + riseToEnd;
        float totalTime = 0.6f + 2.5f + 4f + 2.5f + 0.6f;

        Debug.Log(
            $"[WaterfrontTeleporterSetup] 설정 완료!\n" +
            $"  ══════════ 기준 오브젝트 ══════════\n" +
            $"  텔레포터: {teleporterPos}\n" +
            $"  Stool A:  {stoolA.position} (표면 Y={surfaceYA:F3})\n" +
            $"  Stool B:  {stoolB.position} (표면 Y={surfaceYB:F3})\n" +
            $"  Stool 중간점: {stoolMid} (표면 Y={avgSurfaceY:F3})\n" +
            $"  ══════════ 카메라 경로 (3-Waypoint) ══════════\n" +
            $"  StartShot:  pos={startPos} rot={startRot.eulerAngles}\n" +
            $"  RisePoint:  pos={risePos} rot={riseRot.eulerAngles}\n" +
            $"  EndShot:    pos={endPos} rot={endRot.eulerAngles}\n" +
            $"  ══════════ 메트릭 ══════════\n" +
            $"  상승 높이: {riseHeight:F2}m (Y {headY:F2} → {riseY:F2})\n" +
            $"  Start→Rise: {startToRise:F2}m / 2.5s (수직 상승)\n" +
            $"  Rise→End:   {riseToEnd:F2}m / 4.0s (수평 이동, Y=15 유지)\n" +
            $"  총 이동: {totalMove:F2}m / 6.5s\n" +
            $"  Hold: 2.5s (텔레포터 활성화 감상)\n" +
            $"  전체 컷씬: ~{totalTime:F1}s (Fade 포함)\n" +
            $"  VFX 활성화: EndShot 도달 후 즉시\n" +
            $"  ══════════ 곡선 경로 ══════════\n" +
            $"  Corner Blend Distance: 3m\n" +
            $"  Movement Ease Curve: EaseInOut(0,0 → 1,1)\n" +
            $"  보간: Quadratic Bézier + Arc-Length 매개변수화\n" +
            $"  ══════════ 기타 ══════════\n" +
            $"  VFX: FireCircle={fireCircle != null}, Splash={splash != null}\n" +
            $"  AudioClip: {(clip != null ? clip.name : "없음")}\n" +
            $"  ══════════ 컷씬 연결 ══════════\n" +
            $"  Stage3RiverCutscene: {(riverCutscene != null ? riverCutscene.name : "없음")}\n" +
            $"  연결 방식: RiverCutscene.PlayRoutine → PlayInSequenceRoutine (코드 체이닝)\n" +
            $"  ══════════ 텔레포터 잠금 ══════════\n" +
            $"  ZoneTeleporter: {(zoneTeleporter != null ? "연결됨" : "없음")}\n" +
            $"  잠금 조건: requiredWaterfrontCutscene → CutsceneFinished 이벤트로 해제\n" +
            $"  컷씬 중 탑승 차단: Stage3RiverCutscene.IsPlaying + WaterfrontTeleporterCutscene.IsPlaying",
            managerGO);
    }

    // ── Stool 표면 높이 계산 ─────────────────────────────────

    private static float ComputeMeshSurfaceY(Transform obj)
    {
        MeshFilter mf = obj.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            float meshTopLocal = mf.sharedMesh.bounds.max.y;
            return obj.position.y + meshTopLocal * obj.lossyScale.y;
        }

        return obj.position.y + 0.36f;
    }

    // ── 경로 장애물 검사 ─────────────────────────────────────

    private static void CheckPathObstacle(string segmentName, Vector3 from, Vector3 to)
    {
        if (Physics.Linecast(from, to, out RaycastHit hit))
        {
            Debug.LogWarning(
                $"[WaterfrontTeleporterSetup] 경로 장애물 감지! " +
                $"{segmentName} 구간에서 '{hit.collider.name}'과 충돌 (거리 {hit.distance:F2}m, " +
                $"충돌 지점 {hit.point}). Inspector에서 위치를 조정하세요.");
        }
    }

    private static Quaternion LookAtTarget(Vector3 from, Vector3 target)
    {
        Vector3 dir = target - from;
        if (dir.sqrMagnitude < 0.001f)
            return Quaternion.identity;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    // ── Hierarchy 경로 ───────────────────────────────────────

    private static string GetHierarchyPath(Transform t)
    {
        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }

    // ── 씬 관리 ──────────────────────────────────────────────

    private static Scene EnsureScene()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid() && scene.path == ScenePath)
            return scene;

        return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    // ── 검색 유틸리티 ────────────────────────────────────────

    private static T FindObjectByName<T>(string name) where T : Component
    {
        T[] objects = Object.FindObjectsByType<T>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (T obj in objects)
        {
            if (obj != null && obj.name == name)
                return obj;
        }
        return null;
    }

    private static Transform FindTransformByName(string name)
    {
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            Transform found = FindTransformRecursive(root.transform, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Transform[] FindAllTransformsByName(string name)
    {
        List<Transform> results = new List<Transform>();
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject root in roots)
            FindAllRecursive(root.transform, name, results);
        return results.ToArray();
    }

    private static Transform FindTransformRecursive(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        foreach (Transform child in parent)
        {
            Transform found = FindTransformRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static void FindAllRecursive(
        Transform parent, string name, List<Transform> results)
    {
        if (parent.name == name)
            results.Add(parent);

        foreach (Transform child in parent)
            FindAllRecursive(child, name, results);
    }

    private static GameObject FindChildGameObject(Transform parent, string name)
    {
        foreach (Transform child in
                 parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child.gameObject;
        }
        return null;
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing;

        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(child, $"Create {name}");
        return child.transform;
    }

    // ── SerializedObject 헬퍼 ────────────────────────────────

    private static void SetObjectField(
        SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null)
            prop.objectReferenceValue = value;
    }

    private static void SetFloatField(
        SerializedObject so, string propertyName, float value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null)
            prop.floatValue = value;
    }

    private static void SetBoolField(
        SerializedObject so, string propertyName, bool value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop != null)
            prop.boolValue = value;
    }
}
#endif
