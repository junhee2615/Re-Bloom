#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mission 3-3 온실 복구 컷씬 자동 설정 에디터 도구.
/// ReBloom > Setup > Configure Greenhouse Clear Cutscene 에서 실행.
///
/// Stage3 씬의 각 복구 오브젝트 월드 좌표 기준으로 Shot Transform을 자동 배치.
/// 여러 번 실행해도 이미 존재하는 Shot의 위치/회전은 보존한다.
/// </summary>
public static class ConfigureGreenhouseClearCutscene
{
    private const string ScenePath = "Assets/ReBloom/Scenes/Stage3.unity";
    private const string ManagerName = "GreenhouseClearCutsceneManager";

    private const string ElectricityShotName = "GreenhouseElectricityShot";
    private const string WaterFlowerShotName  = "GreenhouseWaterFlowerShot";
    private const string TreeShotName         = "GreenhouseTreeShot";
    private const string FinalShotName        = "GreenhouseFinalShot";

    // Stage3_Environment가 원점에 있으므로 분석값 그대로 사용
    private static readonly Vector3 ElectricityCorePos = new Vector3(-14.51f, 39.83f, 109.89f);
    // ElectricityCore 회전 ~Y120° → 정면 방향 (sin120°, 0, cos120°) ≈ (0.866, 0, -0.5)
    private static readonly Vector3 ElectricityCoreForward = new Vector3(0.866f, 0f, -0.5f);

    private static readonly Vector3 WaterCorePos   = new Vector3(19.2f, 40f, 110.11f);
    private static readonly Vector3 EndingCorePos  = new Vector3(1.51f, 39.99f, 121.39f);

    private const float EyeHeight = 1.6f;

    [MenuItem("ReBloom/Setup/Configure Greenhouse Clear Cutscene")]
    public static void Configure()
    {
        Scene scene = EnsureScene();
        if (!scene.IsValid())
            return;

        // ── 매니저 GameObject 생성/갱신 ───────────────────────
        GreenhouseClearCutscene manager =
            FindObjectByName<GreenhouseClearCutscene>(ManagerName);

        GameObject managerGO;
        if (manager != null)
        {
            managerGO = manager.gameObject;
            Debug.Log($"[GreenhouseSetup] 기존 '{ManagerName}'을 갱신합니다.", managerGO);
        }
        else
        {
            managerGO = new GameObject(ManagerName);
            managerGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            manager = managerGO.AddComponent<GreenhouseClearCutscene>();
            Undo.RegisterCreatedObjectUndo(managerGO, "Create Greenhouse Clear Cutscene");
        }

        // ── Shot 위치 계산 ─────────────────────────────────────

        // ElectricityShot: 전기 코어 정면 2.5m + 눈높이
        Vector3 electricityPos =
            ElectricityCorePos + ElectricityCoreForward * 2.5f + Vector3.up * EyeHeight;
        // 카메라가 코어를 바라본다
        Quaternion electricityRot = FlatLookAt(
            electricityPos,
            new Vector3(ElectricityCorePos.x, electricityPos.y, ElectricityCorePos.z));

        // WaterFlowerShot: 중앙 남쪽에서 물 코어(우측)와 꽃 개화 영역을 함께 바라봄
        Vector3 waterFlowerPos = new Vector3(8f, ElectricityCorePos.y + EyeHeight, 106f);
        // 물 코어 방향으로 시선 고정
        Quaternion waterFlowerRot = FlatLookAt(
            waterFlowerPos,
            new Vector3(WaterCorePos.x, waterFlowerPos.y, WaterCorePos.z));

        // TreeShot: EndingCore 나무를 남서쪽에서 바라봄
        Vector3 treeShotPos = new Vector3(-2f, EndingCorePos.y + EyeHeight, 116f);
        Quaternion treeShotRot = FlatLookAt(
            treeShotPos,
            new Vector3(EndingCorePos.x, treeShotPos.y, EndingCorePos.z));

        // FinalShot: 나무 중심에서 더 뒤로 물러나 온실 전체 조망
        Vector3 finalShotPos = new Vector3(-2f, EndingCorePos.y + EyeHeight + 3f, 103f);
        // 온실 중심(EndingCore 방향)을 향해 시선 고정
        Quaternion finalShotRot = FlatLookAt(
            finalShotPos,
            new Vector3(EndingCorePos.x, finalShotPos.y - 2f, EndingCorePos.z));

        // ── Shot Transform 생성/보존 ─────────────────────────
        bool createdE = FindOrCreateChild(managerGO.transform, ElectricityShotName,
            electricityPos, electricityRot, out Transform electricityShotT);
        bool createdW = FindOrCreateChild(managerGO.transform, WaterFlowerShotName,
            waterFlowerPos, waterFlowerRot, out Transform waterFlowerShotT);
        bool createdT = FindOrCreateChild(managerGO.transform, TreeShotName,
            treeShotPos, treeShotRot, out Transform treeShotT);
        bool createdF = FindOrCreateChild(managerGO.transform, FinalShotName,
            finalShotPos, finalShotRot, out Transform finalShotT);

        // ── 컴포넌트 프로퍼티 설정 ────────────────────────────
        SerializedObject so = new SerializedObject(manager);

        SetObjectField(so, "electricityShot", electricityShotT);
        SetObjectField(so, "waterFlowerShot", waterFlowerShotT);
        SetObjectField(so, "treeShot",        treeShotT);
        SetObjectField(so, "finalShot",       finalShotT);

        // 타이밍 기본값 설정 (기존값 덮어씀 — Inspector에서 개별 조정 가능)
        SetFloatField(so, "electricityHoldDuration",    2f);
        SetFloatField(so, "moveToWaterFlowerDuration",  3f);
        SetFloatField(so, "waterFlowerHoldDuration",    2f);
        SetFloatField(so, "moveToTreeDuration",         3f);
        SetFloatField(so, "treeHoldDuration",           2f);
        SetFloatField(so, "moveToFinalDuration",        5f);
        SetFloatField(so, "finalHoldDuration",          2f);
        SetFloatField(so, "fadeDuration",               0.5f);

        SetBoolField(so, "disableResonance",    true);
        SetBoolField(so, "disableBuiltinFog",   true);
        SetBoolField(so, "hideRemoteAvatars",   true);
        SetBoolField(so, "lockLocomotion",      true);
        SetBoolField(so, "hideTutorialCanvas",  true);

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);

        // ── 결과 출력 ─────────────────────────────────────────
        float totalDuration = 0.5f + 0.5f   // FadeOut + FadeIn
            + 2f                             // electricityHold
            + 3f + 2f                        // moveToWaterFlower + waterFlowerHold
            + 3f + 2f                        // moveToTree + treeHold
            + 5f + 2f                        // moveToFinal + finalHold
            + 0.5f + 0.5f;                   // FadeOut + FadeIn

        Debug.Log(
            $"[GreenhouseSetup] 설정 완료!\n" +
            $"  ══════════ Shot 위치 ══════════\n" +
            $"  ElectricityShot: {electricityPos:F2} " +
            $"{(createdE ? "(신규)" : "(기존 위치 보존)")}\n" +
            $"  WaterFlowerShot: {waterFlowerPos:F2} " +
            $"{(createdW ? "(신규)" : "(기존 위치 보존)")}\n" +
            $"  TreeShot:        {treeShotPos:F2} " +
            $"{(createdT ? "(신규)" : "(기존 위치 보존)")}\n" +
            $"  FinalShot:       {finalShotPos:F2} " +
            $"{(createdF ? "(신규)" : "(기존 위치 보존)")}\n" +
            $"  ══════════ 타이밍 ══════════\n" +
            $"  ElectricityHold: 2s\n" +
            $"  WaterFlower 이동/Hold: 3s / 2s\n" +
            $"  Tree 이동/Hold: 3s / 2s\n" +
            $"  Final 이동(풀백)/Hold: 5s / 2s\n" +
            $"  전체 컷씬: ~{totalDuration:F1}s (Fade 포함)\n" +
            $"  ══════════ 사용 방법 ══════════\n" +
            $"  1. Stage3.unity Play Mode 진입\n" +
            $"  2. Hierarchy에서 '{ManagerName}' 선택\n" +
            $"  3. Inspector > GreenhouseClearCutscene 헤더 우클릭\n" +
            $"  4. 'Play Greenhouse Clear Cutscene' 선택\n" +
            $"  ══════════ Shot 조정 ══════════\n" +
            $"  - Scene 뷰에서 각 Shot Transform을 직접 이동/회전\n" +
            $"  - Gizmos(노랑/청록/초록/빨강 구체 + 화살표)로 경로 확인\n" +
            $"  - Configure 재실행 시 기존 Shot 위치는 보존됨",
            managerGO);
    }

    // Yaw-only LookAt — Pitch 없이 수평 시선만 계산
    private static Quaternion FlatLookAt(Vector3 from, Vector3 target)
    {
        Vector3 dir = new Vector3(target.x - from.x, 0f, target.z - from.z);
        if (dir.sqrMagnitude < 0.001f)
            return Quaternion.identity;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    // ── 씬 관리 ─────────────────────────────────────────────

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
        T[] all = Object.FindObjectsByType<T>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (T obj in all)
            if (obj != null && obj.name == name) return obj;
        return null;
    }

    /// <summary>
    /// 자식이 없으면 newPos/newRot으로 생성 (created=true).
    /// 이미 있으면 기존 위치/회전 보존 (created=false).
    /// </summary>
    private static bool FindOrCreateChild(
        Transform parent, string childName,
        Vector3 newPos, Quaternion newRot,
        out Transform result)
    {
        Transform existing = parent.Find(childName);
        if (existing != null)
        {
            result = existing;
            return false;
        }

        var go = new GameObject(childName);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(newPos, newRot);
        Undo.RegisterCreatedObjectUndo(go, $"Create {childName}");
        result = go.transform;
        return true;
    }

    // ── SerializedObject 헬퍼 ────────────────────────────────

    private static void SetObjectField(SerializedObject so, string prop, Object value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null) p.objectReferenceValue = value;
    }

    private static void SetFloatField(SerializedObject so, string prop, float value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null) p.floatValue = value;
    }

    private static void SetBoolField(SerializedObject so, string prop, bool value)
    {
        SerializedProperty p = so.FindProperty(prop);
        if (p != null) p.boolValue = value;
    }
}
#endif
