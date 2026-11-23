using ReBloom.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 태양광 시설 전체 클리어 컷씬 매니저를 자동 구성하는 Editor 메뉴.
///
/// 메뉴:
///   Re_Bloom / Stage3 / Facility Clear Cutscene Setup - Apply
///   Re_Bloom / Stage3 / Facility Clear Cutscene - Preview (Play Mode)
///   Re_Bloom / Stage3 / Facility Clear Cutscene - Apply Persistent (Play Mode)
/// </summary>
public static class SolarFacilityClearCutsceneSetup
{
    private const string ManagerName = "SolarFacilityClearCutsceneManager";

    [MenuItem("Re_Bloom/Stage3/Facility Clear Cutscene Setup - Apply")]
    private static void Apply()
    {
        // 1. 매니저 찾거나 생성
        SolarFacilityClearCutscene manager = Object.FindFirstObjectByType<SolarFacilityClearCutscene>();

        if (manager == null)
        {
            GameObject go = new GameObject(ManagerName);
            manager = go.AddComponent<SolarFacilityClearCutscene>();
            Undo.RegisterCreatedObjectUndo(go, "Create Facility Clear Cutscene Manager");
            Debug.Log($"[FacilityClearSetup] '{ManagerName}' 생성.", go);
        }
        else
        {
            Debug.Log($"[FacilityClearSetup] 기존 매니저 사용: {manager.gameObject.name}", manager);
        }

        SerializedObject so = new SerializedObject(manager);

        // 2. ShotPoint 연결
        SerializedProperty shotPointProp = so.FindProperty("shotPoint");
        if (shotPointProp.objectReferenceValue == null)
        {
            GameObject shotPointGO = GameObject.Find("SolarFacilityClearShotPoint");
            if (shotPointGO != null)
            {
                shotPointProp.objectReferenceValue = shotPointGO.transform;
                Debug.Log($"[FacilityClearSetup] ShotPoint 연결: {shotPointGO.name}", shotPointGO);
            }
            else
            {
                Debug.LogWarning("[FacilityClearSetup] 'SolarFacilityClearShotPoint'를 찾지 못했습니다. " +
                                 "Re_Bloom → Stage3 → Create Facility Clear Shot Point를 먼저 실행하세요.");
            }
        }

        // 3. RepairCutscene 연결
        SerializedProperty repairCutsceneProp = so.FindProperty("repairCutscene");
        if (repairCutsceneProp.objectReferenceValue == null)
        {
            SolarPanelRepairCutscene repairCs = Object.FindFirstObjectByType<SolarPanelRepairCutscene>();
            if (repairCs != null)
            {
                repairCutsceneProp.objectReferenceValue = repairCs;
                Debug.Log($"[FacilityClearSetup] RepairCutscene 연결: {repairCs.gameObject.name}", repairCs);
            }
        }

        // 4. 패널 배열 구성
        SerializedProperty panelsProp = so.FindProperty("panels");

        SolarReflector[] allReflectors = Object.FindObjectsByType<SolarReflector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int existingCount = panelsProp.arraySize;
        int added = 0;

        foreach (SolarReflector r in allReflectors)
        {
            if (!r.broken) continue;

            // 중복 검사
            bool found = false;
            for (int i = 0; i < panelsProp.arraySize; i++)
            {
                SerializedProperty entry = panelsProp.GetArrayElementAtIndex(i);
                Object existRef = entry.FindPropertyRelative("reflector").objectReferenceValue;
                if (existRef != null && existRef.GetInstanceID() == r.GetInstanceID())
                {
                    found = true;
                    break;
                }
            }
            if (found) continue;

            // CleanView 찾기
            SolarPanelCleanView cleanView = r.GetComponentInParent<SolarPanelCleanView>();
            if (cleanView == null)
                cleanView = r.transform.root.GetComponentInChildren<SolarPanelCleanView>(true);

            // Particle 찾기
            Transform particleRoot = cleanView != null ? cleanView.transform : r.transform;
            SolarFacilityClearParticle particle =
                particleRoot.GetComponentInChildren<SolarFacilityClearParticle>(true);

            int index = panelsProp.arraySize;
            panelsProp.InsertArrayElementAtIndex(index);
            SerializedProperty newEntry = panelsProp.GetArrayElementAtIndex(index);

            newEntry.FindPropertyRelative("reflector").objectReferenceValue = r;
            newEntry.FindPropertyRelative("cleanView").objectReferenceValue = cleanView;
            newEntry.FindPropertyRelative("particle").objectReferenceValue = particle;

            added++;
            Debug.Log($"  패널 추가: {r.name} (cleanView={cleanView?.name ?? "없음"}, " +
                      $"particle={particle?.name ?? "없음"})", r);
        }

        // 5. 기존 항목의 빈 particle 보완
        for (int i = 0; i < panelsProp.arraySize; i++)
        {
            SerializedProperty entry = panelsProp.GetArrayElementAtIndex(i);
            SerializedProperty particleProp = entry.FindPropertyRelative("particle");

            if (particleProp.objectReferenceValue != null) continue;

            SolarReflector reflector =
                entry.FindPropertyRelative("reflector").objectReferenceValue as SolarReflector;
            if (reflector == null) continue;

            SolarPanelCleanView cv = reflector.GetComponentInParent<SolarPanelCleanView>();
            Transform root = cv != null ? cv.transform : reflector.transform;
            SolarFacilityClearParticle particle =
                root.GetComponentInChildren<SolarFacilityClearParticle>(true);

            if (particle != null)
            {
                particleProp.objectReferenceValue = particle;
                Debug.Log($"  기존 항목 [{i}] particle 보완: {particle.name}", particle);
            }
        }

        // 6. glowColor를 개별 복구 컷씬과 동기화
        SolarPanelRepairCutscene repairCutscene =
            repairCutsceneProp.objectReferenceValue as SolarPanelRepairCutscene;
        if (repairCutscene != null)
        {
            SerializedObject repairSo = new SerializedObject(repairCutscene);
            Color repairGlowColor = repairSo.FindProperty("glowColor").colorValue;
            so.FindProperty("glowColor").colorValue = repairGlowColor;
            Debug.Log($"[FacilityClearSetup] glowColor을 개별 복구 컷씬과 동기화: {repairGlowColor}");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        Scene scene = manager.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[FacilityClearSetup] 완료. 패널 {panelsProp.arraySize}개 등록 " +
                  $"(신규 {added}개). 씬 저장됨.");
    }

    [MenuItem("Re_Bloom/Stage3/Facility Clear Cutscene - Preview (Play Mode)")]
    private static void PreviewPlayMode()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[FacilityClearSetup] Play 모드에서만 미리보기할 수 있습니다.");
            return;
        }

        SolarFacilityClearCutscene manager = Object.FindFirstObjectByType<SolarFacilityClearCutscene>();
        if (manager == null)
        {
            Debug.LogError("[FacilityClearSetup] 씬에서 SolarFacilityClearCutscene을 찾지 못했습니다. " +
                           "Setup - Apply를 먼저 실행하세요.");
            return;
        }

#if UNITY_EDITOR
        manager.EditorTestPlay();
        Debug.Log("[FacilityClearSetup] 전체 클리어 컷씬 미리보기 시작.");
#endif
    }

    [MenuItem("Re_Bloom/Stage3/Facility Clear Cutscene - Apply Persistent (Play Mode)")]
    private static void ApplyPersistentPlayMode()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[FacilityClearSetup] Play 모드에서만 실행할 수 있습니다.");
            return;
        }

        SolarFacilityClearCutscene manager = Object.FindFirstObjectByType<SolarFacilityClearCutscene>();
        if (manager == null)
        {
            Debug.LogError("[FacilityClearSetup] 씬에서 SolarFacilityClearCutscene을 찾지 못했습니다.");
            return;
        }

#if UNITY_EDITOR
        manager.EditorTestApplyPersistent();
        Debug.Log("[FacilityClearSetup] 영구 상태(발광 + 파티클) 적용 완료.");
#endif
    }
}
