#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mission 3-1 물 코어 클리어 컷씬 자동 설정 에디터 도구.
/// ReBloom > Setup > Configure Water Core Clear Cutscene 에서 실행.
///
/// WaterCore(19.2, 40, 110.11) → BabblingWater 방향 카메라 경로 자동 계산.
/// 여러 번 실행해도 오브젝트가 중복 생성되지 않는다.
/// </summary>
public static class ConfigureWaterCoreClearCutscene
{
    private const string ScenePath = "Assets/ReBloom/Scenes/Stage3.unity";
    private const string ManagerName = "WaterCoreClearCutsceneManager";
    private const string StartShotName = "WaterCoreClearStartShot";
    private const string MidWaypointName = "WaterCoreClearMidWaypoint";
    private const string EndShotName = "WaterCoreClearEndShot";
    private const string WaterCoreName = "WaterCore";
    private const string WaterCoreHandleName = "WaterCoreHandle_low";
    private const string BabblingWaterName = "BabblingWater_low";

    private const float HeadHeight = 1.6f;
    private const float HandleFrontDist = 1.5f; // 밸브 정면 카메라 거리 (m)
    private const float SideOffset = 3f;

    [MenuItem("ReBloom/Setup/Configure Water Core Clear Cutscene")]
    public static void Configure()
    {
        Scene scene = EnsureScene();
        if (!scene.IsValid())
            return;

        // ── 기준 오브젝트 검색 ─────────────────────────────

        Transform waterCore = FindTransformByName(WaterCoreName);
        if (waterCore == null)
        {
            Debug.LogError(
                $"[WaterCoreSetup] '{WaterCoreName}'을 씬에서 찾지 못했습니다. " +
                "Stage3_Environment 프리팹의 WaterCore가 활성화되어 있는지 확인하세요.");
            return;
        }

        // 밸브 손잡이 오브젝트 — StartShot 기준
        Transform handle = FindTransformByName(WaterCoreHandleName);
        if (handle == null)
        {
            // 손잡이를 못 찾으면 WaterCore 위치로 대체
            Debug.LogWarning(
                $"[WaterCoreSetup] '{WaterCoreHandleName}'을 찾지 못했습니다. " +
                "WaterCore 위치를 StartShot 기준으로 사용합니다.");
        }

        // BabblingWater_low 중 WaterCore의 자식 또는 근처에 있는 것 선택
        Transform babblingWater = FindBabblingWaterNear(waterCore);
        if (babblingWater == null)
        {
            Debug.LogWarning(
                $"[WaterCoreSetup] '{BabblingWaterName}'을 찾지 못했습니다. " +
                "EndShot 위치를 WaterCore 기준 기본값으로 설정합니다.");
        }

        Vector3 corePos = waterCore.position;
        Vector3 handlePos = handle != null ? handle.position : corePos;
        Vector3 babblingPos = babblingWater != null
            ? babblingWater.position
            : corePos + new Vector3(-8f, -1f, -11f);

        // ── 카메라 경로 계산 ───────────────────────────────
        //
        // 플레이어 눈높이: corePos.y + HeadHeight (플랫폼 위 서 있는 기준)
        // 모든 웨이포인트 Y를 동일하게 고정 → 수직 흔들림 없음 (VR 멀미 방지)
        //
        // 외측 방향: 코어 중심 → 핸들 방향(수평 성분만).
        //   WaterCore.forward는 기계 내부를 향할 수 있으므로 사용하지 않는다.
        //   손잡이는 항상 기계 외부에 돌출되어 있으므로 이 방향이 "플레이어가 서는 쪽".
        //
        // StartShot: 핸들에서 외측 방향으로 HandleFrontDist(1.5m) + 기계 반경만큼 더 이동.
        //   MeshRenderer Bounds를 써서 기계 외부 표면까지 거리를 구한 뒤 1.5m 추가.

        // 고정 눈높이 (모든 웨이포인트 공통)
        float eyeY = corePos.y + HeadHeight;

        // 외측 방향: 코어 중심 → 핸들 (수평 성분, 정규화)
        Vector3 coreToHandle = Vector3.ProjectOnPlane(handlePos - corePos, Vector3.up);
        Vector3 outwardDir;
        if (coreToHandle.sqrMagnitude > 0.01f)
        {
            outwardDir = coreToHandle.normalized;
        }
        else
        {
            // 핸들이 코어 정중앙에 있는 예외 케이스 — 폴백으로 WaterCore.right 사용
            outwardDir = Vector3.ProjectOnPlane(waterCore.right, Vector3.up).normalized;
            Debug.LogWarning(
                "[WaterCoreSetup] 핸들과 코어가 같은 위치 — WaterCore.right을 외측 방향으로 사용합니다.");
        }

        // WaterCore 계층 MeshRenderer의 합산 Bounds로 기계 표면 거리 계산
        Renderer[] coreRenderers = waterCore.GetComponentsInChildren<Renderer>(true);
        Bounds machineBounds = new Bounds(corePos, Vector3.zero);
        foreach (Renderer r in coreRenderers)
            machineBounds.Encapsulate(r.bounds);

        // corePos 기준 외측 방향 표면까지의 거리
        // ※ IntersectRay는 Ray 원점이 Bounds 내부면 항상 true를 반환하므로
        //   원점을 Bounds 바깥쪽 먼 지점에서 역방향으로 쏜다.
        float machineSurfaceDist = Mathf.Max(
            Vector3.ProjectOnPlane(machineBounds.extents, Vector3.up).magnitude,
            1f);  // 최소 1m
        Vector3 farPoint = corePos + outwardDir * (machineSurfaceDist * 3f);
        Ray inwardRay = new Ray(farPoint, -outwardDir);
        if (machineBounds.IntersectRay(inwardRay, out float hitDist))
        {
            // farPoint에서 표면까지의 거리 → 코어에서 표면까지의 거리
            machineSurfaceDist = Mathf.Max(machineSurfaceDist * 3f - hitDist, 0.5f);
        }

        // StartShot: 기계 표면 + HandleFrontDist(1.5m), 눈높이
        Vector3 startPos = corePos + outwardDir * (machineSurfaceDist + HandleFrontDist);
        startPos.y = eyeY;

        // 카메라가 콜라이더 안에 들어가지 않는지 확인
        float checkRadius = 0.3f;
        if (Physics.CheckSphere(startPos, checkRadius))
        {
            Debug.LogWarning(
                $"[WaterCoreSetup] StartShot({startPos})이 콜라이더와 겹칩니다! " +
                $"기계 표면 거리({machineSurfaceDist:F2}m) + {HandleFrontDist}m 외부에 위치해야 합니다. " +
                "Inspector에서 위치를 수동 조정하거나 HandleFrontDist를 늘리세요.");
            // 경고 후 자동 보정: 0.5m 더 밀어냄
            startPos += outwardDir * 0.5f;
        }

        // 손잡이 중심을 바라보도록 (Yaw only)
        Quaternion startRot = FlatLookAt(startPos, new Vector3(handlePos.x, eyeY, handlePos.z));

        // 배관 방향: 핸들 → BabblingWater
        Vector3 waterDir = Vector3.ProjectOnPlane(
            babblingPos - handlePos, Vector3.up).normalized;
        Vector3 sideDir = Vector3.Cross(waterDir, Vector3.up).normalized;

        // MidWaypoint: 핸들과 BabblingWater 중간, 측면 SideOffset, 같은 높이
        Vector3 midPos = new Vector3(
            Vector3.Lerp(handlePos, babblingPos, 0.5f).x + sideDir.x * SideOffset,
            eyeY,
            Vector3.Lerp(handlePos, babblingPos, 0.5f).z + sideDir.z * SideOffset);
        Quaternion midRot = FlatLookAt(midPos, new Vector3(babblingPos.x, eyeY, babblingPos.z));

        // EndShot: BabblingWater 측면 (SideOffset+1)m, 같은 높이
        Vector3 endPos = new Vector3(
            babblingPos.x + sideDir.x * (SideOffset + 1f),
            eyeY,
            babblingPos.z + sideDir.z * (SideOffset + 1f));
        // 중앙 나무 방향으로 약간 돌리기 위해 BabblingWater를 바라봄
        Quaternion endRot = FlatLookAt(endPos, new Vector3(babblingPos.x, eyeY, babblingPos.z));

        Debug.Log(
            $"[WaterCoreSetup] 기준 오브젝트:\n" +
            $"  WaterCore: {corePos}\n" +
            $"  WaterCoreHandle: {handlePos}\n" +
            $"  외측 방향(outwardDir): {outwardDir}\n" +
            $"  기계 표면 거리: {machineSurfaceDist:F2}m\n" +
            $"  BabblingWater: {babblingPos} ({(babblingWater != null ? babblingWater.name : "기본값")})\n" +
            $"  고정 눈높이 Y: {eyeY:F2}\n" +
            $"  StartShot 최종 위치: {startPos}");

        // ── 경로 장애물 검사 ───────────────────────────────
        CheckPathObstacle("StartShot→MidWaypoint", startPos, midPos);
        CheckPathObstacle("MidWaypoint→EndShot", midPos, endPos);

        // ── 매니저 GameObject 생성/갱신 ───────────────────
        WaterCoreClearCutscene manager =
            FindObjectByName<WaterCoreClearCutscene>(ManagerName);

        GameObject managerGO;
        if (manager != null)
        {
            managerGO = manager.gameObject;
            Debug.Log(
                $"[WaterCoreSetup] 기존 '{ManagerName}'을 갱신합니다.", managerGO);
        }
        else
        {
            managerGO = new GameObject(ManagerName);
            managerGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            manager = managerGO.AddComponent<WaterCoreClearCutscene>();
            Undo.RegisterCreatedObjectUndo(managerGO, "Create Water Core Clear Cutscene");
        }

        // ── 웨이포인트 생성/갱신 ───────────────────────────
        Transform startShotT = FindOrCreateChild(managerGO.transform, StartShotName);
        startShotT.SetPositionAndRotation(startPos, startRot);

        Transform midWaypointT = FindOrCreateChild(managerGO.transform, MidWaypointName);
        midWaypointT.SetPositionAndRotation(midPos, midRot);

        Transform endShotT = FindOrCreateChild(managerGO.transform, EndShotName);
        endShotT.SetPositionAndRotation(endPos, endRot);

        // ── BabblingWater_low 오브젝트 수집 ───────────────
        //   WaterCore 근방의 모든 BabblingWater_low를 waterFlowObjects에 연결
        Transform[] allBabbling = FindAllTransformsByName(BabblingWaterName);
        var nearBabbling = new System.Collections.Generic.List<GameObject>();
        foreach (Transform t in allBabbling)
        {
            if (Vector3.Distance(t.position, corePos) < 30f)
                nearBabbling.Add(t.gameObject);
        }

        // 모체(부모) BabblingWater_low 컨테이너가 있으면 그것만 넣음
        //   씬 오버라이드 오디오 소스가 있는 오브젝트를 포함시켜야 하므로
        //   부모 오브젝트가 비활성 상태인 것을 찾는다
        Transform parentContainer = FindBabblingContainerNear(waterCore);
        var waterFlowList = new System.Collections.Generic.List<GameObject>();
        if (parentContainer != null)
            waterFlowList.Add(parentContainer.gameObject);
        else
            waterFlowList.AddRange(nearBabbling);

        // ── 컴포넌트 프로퍼티 설정 ────────────────────────
        SerializedObject so = new SerializedObject(manager);

        SetObjectField(so, "startShot", startShotT);
        SetObjectField(so, "midWaypoint", midWaypointT);
        SetObjectField(so, "endShot", endShotT);

        SetFloatField(so, "fadeDuration", 0.5f);
        SetFloatField(so, "startHoldDuration", 1f);
        SetFloatField(so, "moveDuration", 6f);
        SetFloatField(so, "endHoldDuration", 3f);
        SetFloatField(so, "cornerBlendDistance", 2f);
        SetFloatField(so, "waterActivationProgress", 1f);

        SerializedProperty curveProp = so.FindProperty("movementEaseCurve");
        if (curveProp != null)
            curveProp.animationCurveValue = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        SetBoolField(so, "followYaw", true);
        SetBoolField(so, "followPitch", false);
        SetBoolField(so, "lockCameraHeight", true);
        SetBoolField(so, "disableResonance", true);
        SetBoolField(so, "disableBuiltinFog", true);
        SetBoolField(so, "hideRemoteAvatars", true);
        SetBoolField(so, "lockLocomotion", true);
        SetBoolField(so, "hideTutorialCanvas", true);

        SerializedProperty wfoProp = so.FindProperty("waterFlowObjects");
        wfoProp.arraySize = waterFlowList.Count;
        for (int i = 0; i < waterFlowList.Count; i++)
            wfoProp.GetArrayElementAtIndex(i).objectReferenceValue = waterFlowList[i];

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        EditorSceneManager.MarkSceneDirty(scene);

        // ── 결과 출력 ─────────────────────────────────────
        float startToMid = Vector3.Distance(startPos, midPos);
        float midToEnd = Vector3.Distance(midPos, endPos);
        float totalMove = startToMid + midToEnd;

        Debug.Log(
            $"[WaterCoreSetup] 설정 완료!\n" +
            $"  ══════════ 기준 오브젝트 ══════════\n" +
            $"  WaterCore 월드 위치: {corePos}\n" +
            $"  BabblingWater 월드 위치: {babblingPos}\n" +
            $"  배관 방향(단위벡터): {waterDir}\n" +
            $"  측면 방향(단위벡터): {sideDir}\n" +
            $"  ══════════ 카메라 경로 ══════════\n" +
            $"  StartShot:    pos={startPos} rot={startRot.eulerAngles}\n" +
            $"  MidWaypoint:  pos={midPos} rot={midRot.eulerAngles}\n" +
            $"  EndShot:      pos={endPos} rot={endRot.eulerAngles}\n" +
            $"  ══════════ 메트릭 ══════════\n" +
            $"  Start→Mid: {startToMid:F2}m\n" +
            $"  Mid→End:   {midToEnd:F2}m\n" +
            $"  총 이동거리: {totalMove:F2}m / 6s\n" +
            $"  전체 컷씬: ~{0.5f + 1f + 6f + 3f + 0.5f:F1}s (Fade 포함)\n" +
            $"  ══════════ 물줄기 연출 ══════════\n" +
            $"  waterFlowObjects: {waterFlowList.Count}개\n" +
            $"  물줄기 활성화: EndShot 도달 시 (waterActivationProgress=1.0)\n" +
            $"  미리보기 복원: 종료 후 비활성 상태로 복원\n" +
            $"  ══════════ 사용 방법 ══════════\n" +
            $"  1. Stage3.unity Play Mode 진입\n" +
            $"  2. Hierarchy에서 '{ManagerName}' 선택\n" +
            $"  3. Inspector > WaterCoreClearCutscene 헤더 우클릭\n" +
            $"  4. 'Play Water Core Clear Cutscene' 선택\n" +
            $"  ══════════ 주의 ══════════\n" +
            $"  - WaterValveRotate.onValveCleared에는 아직 연결되지 않음\n" +
            $"  - Inspector에서 위치/시간 조정 가능 (Gizmos로 경로 확인)\n" +
            $"  - Corner Blend Distance로 모서리 곡선 조정",
            managerGO);
    }

    // ── BabblingWater 검색 헬퍼 ─────────────────────────────

    private static Transform FindBabblingWaterNear(Transform waterCore)
    {
        Transform[] all = FindAllTransformsByName(BabblingWaterName);
        Transform best = null;
        float bestDist = float.MaxValue;

        foreach (Transform t in all)
        {
            float d = Vector3.Distance(t.position, waterCore.position);
            if (d < 30f && d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        return best;
    }

    // 초기에 비활성화된(SetActive=false) BabblingWater 컨테이너 부모 오브젝트 검색
    private static Transform FindBabblingContainerNear(Transform waterCore)
    {
        Transform[] all = FindAllTransformsByName(BabblingWaterName);
        foreach (Transform t in all)
        {
            float d = Vector3.Distance(t.position, waterCore.position);
            if (d < 30f && !t.gameObject.activeSelf)
                return t;

            // 부모가 비활성화된 경우
            if (t.parent != null && !t.parent.gameObject.activeSelf
                && Vector3.Distance(t.parent.position, waterCore.position) < 30f)
                return t.parent;
        }
        return null;
    }

    // ── 경로 장애물 검사 ─────────────────────────────────────

    private static void CheckPathObstacle(string segName, Vector3 from, Vector3 to)
    {
        if (Physics.Linecast(from, to, out RaycastHit hit))
        {
            Debug.LogWarning(
                $"[WaterCoreSetup] 경로 장애물 감지! {segName} 구간에서 " +
                $"'{hit.collider.name}'와 충돌 (거리 {hit.distance:F2}m, 충돌점 {hit.point}). " +
                "Inspector에서 위치를 수동으로 조정하세요.");
        }
    }

    // Yaw-only LookAt — from과 target의 Y를 맞추어 Pitch 없이 수평 시선만 계산
    private static Quaternion FlatLookAt(Vector3 from, Vector3 target)
    {
        Vector3 dir = new Vector3(target.x - from.x, 0f, target.z - from.z);
        if (dir.sqrMagnitude < 0.001f)
            return Quaternion.identity;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private static Quaternion SafeLookAt(Vector3 from, Vector3 target)
    {
        Vector3 dir = target - from;
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
        var results = new System.Collections.Generic.List<Transform>();
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject root in roots)
            FindAllRecursive(root.transform, name, results);
        return results.ToArray();
    }

    private static Transform FindTransformRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindTransformRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static void FindAllRecursive(
        Transform parent, string name,
        System.Collections.Generic.List<Transform> results)
    {
        if (parent.name == name)
            results.Add(parent);
        foreach (Transform child in parent)
            FindAllRecursive(child, name, results);
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing;

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        return go.transform;
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
