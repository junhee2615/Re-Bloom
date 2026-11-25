#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Mission 3-2 식물 코어 클리어 컷씬 자동 설정 에디터 도구.
/// ReBloom > Setup > Configure Plant Core Clear Cutscene 에서 실행.
///
/// PlantCoreSeedButton 위치를 기준으로 카메라 경로 자동 계산.
/// 여러 번 실행해도 오브젝트가 중복 생성되지 않는다.
/// </summary>
public static class ConfigurePlantCoreClearCutscene
{
    private const string ScenePath = "Assets/ReBloom/Scenes/Stage3.unity";
    private const string ManagerName = "PlantCoreClearCutsceneManager";
    private const string StartShotName = "PlantCoreClearStartShot";
    private const string EndShotName = "PlantCoreClearEndShot";

    // 씨앗 배출구 + 새싹이 모두 보이는 거리 (기계 버튼 면에서 카메라까지, 월드 단위)
    private const float CameraStartDist = 0.5f;
    // 카메라가 전진하는 거리 (월드 단위)
    private const float AdvanceDistance = 0.8f;

    [MenuItem("ReBloom/Setup/Configure Plant Core Clear Cutscene")]
    public static void Configure()
    {
        Scene scene = EnsureScene();
        if (!scene.IsValid())
            return;

        // ── 기준 오브젝트 검색 ─────────────────────────────

        PlantCoreSeedButton seedButton = FindObjectByType<PlantCoreSeedButton>();
        if (seedButton == null)
        {
            Debug.LogError(
                "[PlantCoreSetup] 씬에서 PlantCoreSeedButton을 찾지 못했습니다. " +
                "Stage3.unity가 열려 있고 PlantCoreMission이 활성화되어 있는지 확인하세요.");
            return;
        }

        // ── 씨앗 버튼 타이밍 정보 읽기 ───────────────────
        SerializedObject seedSO = new SerializedObject(seedButton);
        float dropDuration  = seedSO.FindProperty("dropDuration")?.floatValue  ?? 0.74f;
        float waitAfterDrop = seedSO.FindProperty("waitAfterDrop")?.floatValue ?? 0.5f;
        float sinkDuration  = seedSO.FindProperty("sinkDuration")?.floatValue  ?? 1f;
        float growDuration  = seedSO.FindProperty("growDuration")?.floatValue  ?? 2f;
        float cameraMovementDelay = 0f; // 씨앗 낙하와 동시에 카메라 전진
        float moveDuration = dropDuration + waitAfterDrop + sinkDuration + growDuration;

        // ── 카메라 기준 위치 계산 ─────────────────────────
        //
        // 외측 방향: PlantCoreMission 루트 → PlantCoreSeedButton (수평 성분).
        // 버튼은 항상 기계 외부 정면에 있으므로 이 방향이 "플레이어가 서는 쪽".
        //
        // 카메라 높이: 버튼 Y - 약간 아래 → 씨앗(위)과 새싹(아래)이 모두 보임.
        // StartShot: 버튼 위치 + 외측 방향 * CameraStartDist
        // EndShot: StartShot + startForward * AdvanceDistance (기계 쪽으로 전진)

        Vector3 buttonPos = seedButton.transform.position;

        // 머신 중심: 계층에서 버튼과 의미 있게 떨어진 조상 위치를 사용.
        // 씬 루트 전체의 Renderer Bounds를 쓰면 장식용 연꽃 오브젝트가 포함되어
        // Bounds.center가 실제 기계 위치에서 크게 벗어남.
        Transform machineAncestor = seedButton.transform.parent;
        for (int i = 0; i < 4 && machineAncestor?.parent != null; i++)
        {
            Vector3 ancestorXZ = Vector3.ProjectOnPlane(
                machineAncestor.position - buttonPos, Vector3.up);
            if (ancestorXZ.sqrMagnitude > 0.09f) // XZ 거리 0.3m 이상이면 기계 본체 도달
                break;
            machineAncestor = machineAncestor.parent;
        }
        Vector3 machineCenter = machineAncestor != null
            ? machineAncestor.position : buttonPos;

        // 외측 방향 (수평): 머신 중심 → 버튼 위치
        Vector3 toButton = Vector3.ProjectOnPlane(
            buttonPos - machineCenter, Vector3.up);
        Vector3 outwardDir;
        if (toButton.sqrMagnitude > 0.001f)
        {
            outwardDir = toButton.normalized;
        }
        else
        {
            // 폴백: seedButton.transform.forward (버튼 로컬 앞방향)
            outwardDir = Vector3.ProjectOnPlane(
                seedButton.transform.forward, Vector3.up).normalized;
            if (outwardDir.sqrMagnitude < 0.001f)
                outwardDir = Vector3.forward;
            Debug.LogWarning(
                "[PlantCoreSetup] 버튼과 조상 중심이 같은 위치 — " +
                "seedButton.transform.forward를 외측 방향으로 사용합니다.");
        }

        // 카메라 높이: 버튼 Y 그대로 사용 (씨앗 배출구 중심)
        float cameraY = buttonPos.y;

        // StartShot: 버튼 정면 CameraStartDist 거리
        Vector3 startPos = buttonPos + outwardDir * CameraStartDist;
        startPos.y = cameraY;

        // 카메라가 콜라이더 안에 들어가지 않는지 확인
        float checkRadius = 0.1f;
        if (Physics.CheckSphere(startPos, checkRadius))
        {
            Debug.LogWarning(
                $"[PlantCoreSetup] StartShot({startPos})이 콜라이더와 겹칩니다! " +
                "Inspector에서 위치를 수동 조정하거나 CameraStartDist를 늘리세요. " +
                "자동 보정: 0.2m 추가.");
            startPos += outwardDir * 0.2f;
        }

        // StartShot 회전: 버튼 중심을 Yaw-only로 바라봄
        Vector3 lookTarget = new Vector3(buttonPos.x, cameraY, buttonPos.z);
        Quaternion startRot = FlatLookAt(startPos, lookTarget);

        // EndShot: StartShot에서 버튼 쪽으로 AdvanceDistance 전진
        // startRot.forward = lookTarget 방향 = 기계 쪽
        Vector3 endPos = startPos + startRot * Vector3.forward * AdvanceDistance;
        endPos.y = cameraY; // 높이 고정

        // 경로 장애물 검사
        CheckPathObstacle("StartShot→EndShot", startPos, endPos);

        Debug.Log(
            $"[PlantCoreSetup] 기준 오브젝트:\n" +
            $"  PlantCoreSeedButton: {buttonPos}\n" +
            $"  머신 조상 중심: {machineCenter} ({(machineAncestor != null ? machineAncestor.name : "없음")})\n" +
            $"  외측 방향: {outwardDir}\n" +
            $"  카메라 높이 Y: {cameraY:F3}\n" +
            $"  StartShot: {startPos} (rot: {startRot.eulerAngles})\n" +
            $"  EndShot:   {endPos}");

        // ── 매니저 GameObject 생성/갱신 ───────────────────
        PlantCoreClearCutscene manager =
            FindObjectByName<PlantCoreClearCutscene>(ManagerName);

        GameObject managerGO;
        if (manager != null)
        {
            managerGO = manager.gameObject;
            Debug.Log($"[PlantCoreSetup] 기존 '{ManagerName}'을 갱신합니다.", managerGO);
        }
        else
        {
            managerGO = new GameObject(ManagerName);
            managerGO.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            manager = managerGO.AddComponent<PlantCoreClearCutscene>();
            Undo.RegisterCreatedObjectUndo(managerGO, "Create Plant Core Clear Cutscene");
        }

        // ── 웨이포인트 생성/갱신 ───────────────────────────
        Transform startShotT = FindOrCreateChild(managerGO.transform, StartShotName);
        startShotT.SetPositionAndRotation(startPos, startRot);

        Transform endShotT = FindOrCreateChild(managerGO.transform, EndShotName);
        endShotT.SetPositionAndRotation(endPos, startRot); // 같은 방향 유지

        // ── 컴포넌트 프로퍼티 설정 ────────────────────────
        SerializedObject so = new SerializedObject(manager);

        // PlantCoreParticle 컴포넌트 — 없으면 추가
        PlantCoreParticle particle = managerGO.GetComponent<PlantCoreParticle>();
        if (particle == null)
        {
            particle = Undo.AddComponent<PlantCoreParticle>(managerGO);
        }

        SetObjectField(so, "startShot", startShotT);
        SetObjectField(so, "endShot", endShotT);
        SetObjectField(so, "seedButton", seedButton);
        SetObjectField(so, "plantParticle", particle);

        SetFloatField(so, "fadeDuration", 0.5f);
        SetFloatField(so, "startHoldDuration", 0f);
        SetFloatField(so, "moveDuration", moveDuration);
        SetFloatField(so, "endHoldDuration", 1.5f);
        SetFloatField(so, "cameraMovementDelay", cameraMovementDelay);
        SetFloatField(so, "startYawOffsetDeg", 0f); // Configure 재실행 시 런타임 Yaw 오프셋 초기화

        SerializedProperty curveProp = so.FindProperty("movementEaseCurve");
        if (curveProp != null)
            curveProp.animationCurveValue = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        SetBoolField(so, "disableResonance", true);
        SetBoolField(so, "disableBuiltinFog", true);
        SetBoolField(so, "hideRemoteAvatars", true);
        SetBoolField(so, "lockLocomotion", true);
        SetBoolField(so, "hideTutorialCanvas", true);

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        EditorSceneManager.MarkSceneDirty(scene);

        // ── 결과 출력 ─────────────────────────────────────
        // FadeOut + FadeIn + startHold + moveDuration + endHold + FadeOut + FadeIn
        float totalDuration = 0.5f + 0.5f + 0f + moveDuration + 1.5f + 0.5f + 0.5f;

        Debug.Log(
            $"[PlantCoreSetup] 설정 완료!\n" +
            $"  ══════════ 카메라 경로 ══════════\n" +
            $"  StartShot: pos={startPos:F3}, rot={startRot.eulerAngles:F1}\n" +
            $"  EndShot:   pos={endPos:F3}\n" +
            $"  전진 거리: {Vector3.Distance(startPos, endPos):F3}m (목표: {AdvanceDistance}m)\n" +
            $"  ══════════ 타이밍 ══════════\n" +
            $"  Fade: 0.5s\n" +
            $"  시작 Hold: 0s\n" +
            $"  전진 이동: {moveDuration:F2}s " +
            $"(drop={dropDuration}+wait={waitAfterDrop}+sink={sinkDuration}+grow={growDuration})\n" +
            $"  종료 Hold: 1.5s\n" +
            $"  전체 컷씬: ~{totalDuration:F1}s (Fade 포함)\n" +
            $"  ══════════ 사용 방법 ══════════\n" +
            $"  1. Stage3.unity Play Mode 진입\n" +
            $"  2. Hierarchy에서 '{ManagerName}' 선택\n" +
            $"  3. Inspector > PlantCoreClearCutscene 헤더 우클릭\n" +
            $"  4. 'Play Plant Core Clear Cutscene' 선택\n" +
            $"  ══════════ 주의 ══════════\n" +
            $"  - 실제 미션 완료 이벤트에는 아직 연결하지 않음\n" +
            $"  - Inspector에서 위치/시간 조정 가능 (Gizmos로 경로 확인)\n" +
            $"  - 미리보기 종료 후 씨앗/새싹 상태 자동 복원 (반복 테스트 가능)\n" +
            $"  - StartShot/EndShot 위치가 기계 내부에 있으면 수동 조정 필요",
            managerGO);
    }

    // ── 타이밍만 갱신 (위치/회전 유지) ──────────────────────────
    /// <summary>
    /// StartShot/EndShot 위치를 건드리지 않고 씨앗 버튼 타이밍과
    /// 컷씬 타이밍 값만 현재 스크립트 기본값으로 갱신한다.
    /// </summary>
    [MenuItem("ReBloom/Setup/Update Plant Core Timing Only")]
    public static void UpdateTimingOnly()
    {
        Scene scene = EnsureScene();
        if (!scene.IsValid()) return;

        PlantCoreSeedButton seedButton = FindObjectByType<PlantCoreSeedButton>();
        if (seedButton == null)
        {
            Debug.LogError("[PlantCoreSetup] PlantCoreSeedButton을 찾지 못했습니다.");
            return;
        }

        // PlantCoreSeedButton 타이밍 갱신
        SerializedObject seedSO = new SerializedObject(seedButton);
        SetFloatField(seedSO, "waitAfterDrop", 0.5f);
        SetFloatField(seedSO, "sinkDuration",  1f);
        SetFloatField(seedSO, "growDuration",  2f);
        seedSO.ApplyModifiedProperties();
        EditorUtility.SetDirty(seedButton);

        // PlantCoreClearCutscene 타이밍 갱신
        PlantCoreClearCutscene manager =
            FindObjectByName<PlantCoreClearCutscene>(ManagerName);
        if (manager != null)
        {
            float dropDuration  = seedSO.FindProperty("dropDuration")?.floatValue  ?? 0.74f;
            float waitAfterDrop = 0.5f;
            float sinkDuration  = 1f;
            float growDuration  = 2f;
            float moveDuration  = dropDuration + waitAfterDrop + sinkDuration + growDuration;

            SerializedObject so = new SerializedObject(manager);
            SetFloatField(so, "startHoldDuration",  0f);
            SetFloatField(so, "moveDuration",        moveDuration);
            SetFloatField(so, "endHoldDuration",     1.5f);
            SetFloatField(so, "cameraMovementDelay", 0f);

            // PlantCoreParticle 컴포넌트 — 없으면 추가하고 참조 연결
            PlantCoreParticle particle = manager.GetComponent<PlantCoreParticle>()
                ?? manager.gameObject.AddComponent<PlantCoreParticle>();
            SetObjectField(so, "plantParticle", particle);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);

            float total = 0.5f + 0.5f + moveDuration + 1.5f + 0.5f + 0.5f;
            Debug.Log(
                $"[PlantCoreSetup] 타이밍 갱신 완료!\n" +
                $"  waitAfterDrop=0.5  sinkDuration=1  growDuration=2\n" +
                $"  moveDuration={moveDuration:F2}s  endHoldDuration=1.5s\n" +
                $"  전체 컷씬: ~{total:F2}s",
                manager);
        }
        else
        {
            Debug.Log("[PlantCoreSetup] PlantCoreSeedButton 타이밍 갱신 완료. " +
                      $"'{ManagerName}'를 찾지 못해 컷씬 타이밍은 건너뜀.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    // ── 오디오만 연결 (위치/타이밍 유지) ────────────────────────
    /// <summary>
    /// seed_drop.mp3 → PlantCoreSeedButton.seedDropAudio 클립 연결,
    /// alive_decal.mp3 → PlantCoreClearCutscene.sproutGrowClip 연결.
    /// 위치·타이밍·Configure 전체를 다시 실행할 필요가 없다.
    /// </summary>
    [MenuItem("ReBloom/Setup/Setup Plant Core Audio")]
    public static void SetupAudio()
    {
        Scene scene = EnsureScene();
        if (!scene.IsValid()) return;

        // ── 씨앗 착지 효과음 ─────────────────────────────────
        PlantCoreSeedButton seedButton = FindObjectByType<PlantCoreSeedButton>();
        if (seedButton != null)
        {
            AudioClip seedClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                "Assets/ReBloom/Sounds/Stage3/seed_drop.mp3");
            if (seedClip == null)
            {
                Debug.LogError("[PlantCoreSetup] seed_drop.mp3를 찾지 못했습니다.");
            }
            else
            {
                // SerializedObject로 private seedDropAudio 필드에 접근
                SerializedObject seedSO = new SerializedObject(seedButton);
                AudioSource audioSource =
                    seedSO.FindProperty("seedDropAudio").objectReferenceValue as AudioSource;
                if (audioSource != null)
                {
                    Undo.RecordObject(audioSource, "Setup SeedDrop Audio");
                    audioSource.clip        = seedClip;
                    audioSource.volume      = 0.6f;
                    audioSource.loop        = false;
                    audioSource.playOnAwake = false;
                    // spatialBlend 는 기존 AudioSource 설정 유지
                    EditorUtility.SetDirty(audioSource);
                    Debug.Log("[PlantCoreSetup] seed_drop.mp3 → seedDropAudio 연결 완료 (볼륨 0.6).");
                }
                else
                {
                    Debug.LogWarning("[PlantCoreSetup] seedDropAudio AudioSource 참조가 null입니다. " +
                                     "Inspector에서 PlantCoreSeedButton → seedDropAudio를 연결하세요.");
                }
            }
        }

        // ── 새싹 성장 효과음 ─────────────────────────────────
        // 이미 설정된 클립/볼륨은 덮어쓰지 않는다 (사용자 직접 교체값 보호)
        PlantCoreClearCutscene manager =
            FindObjectByName<PlantCoreClearCutscene>(ManagerName);
        if (manager != null)
        {
            SerializedObject so = new SerializedObject(manager);
            var existingClip = so.FindProperty("sproutGrowClip")?.objectReferenceValue as AudioClip;
            if (existingClip != null)
            {
                Debug.Log($"[PlantCoreSetup] sproutGrowClip이 이미 설정됨 ({existingClip.name}) — 덮어쓰지 않음.");
            }
            else
            {
                AudioClip sproutClip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/ReBloom/Sounds/alive_decal.mp3");
                if (sproutClip == null)
                {
                    Debug.LogError("[PlantCoreSetup] alive_decal.mp3를 찾지 못했습니다.");
                }
                else
                {
                    SetObjectField(so, "sproutGrowClip", sproutClip);
                    SetFloatField(so, "sproutGrowVolume", 0.7f);
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);
                    Debug.Log("[PlantCoreSetup] alive_decal.mp3 → sproutGrowClip 연결 완료 (볼륨 0.7).");
                }
            }
        }
        else
        {
            Debug.LogWarning($"[PlantCoreSetup] '{ManagerName}'를 찾지 못했습니다. " +
                             "Configure 메뉴를 먼저 실행하세요.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[PlantCoreSetup] 오디오 설정 완료!");
    }

    // ── 경로 장애물 검사 ─────────────────────────────────────

    private static void CheckPathObstacle(string segName, Vector3 from, Vector3 to)
    {
        if (Physics.Linecast(from, to, out RaycastHit hit))
        {
            Debug.LogWarning(
                $"[PlantCoreSetup] 경로 장애물 감지! {segName} 구간에서 " +
                $"'{hit.collider.name}'와 충돌 (거리 {hit.distance:F3}m, 충돌점 {hit.point}). " +
                "Inspector에서 위치를 수동으로 조정하세요.");
        }
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

    private static T FindObjectByType<T>() where T : Component
    {
        return Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    }

    private static T FindObjectByName<T>(string name) where T : Component
    {
        T[] all = Object.FindObjectsByType<T>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (T obj in all)
            if (obj != null && obj.name == name) return obj;
        return null;
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
