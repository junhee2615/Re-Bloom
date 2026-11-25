#if UNITY_EDITOR
using ReBloom.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 태양광 시설 가동음(EnergyHum)을 3D 공간 음향 AudioSource로 설정하는 에디터 도구.
///
/// 메뉴: Re_Bloom / Stage3 / Setup Solar Facility Energy Hum
///
/// 패널 4개의 중심에 SolarFacilityEnergyHum GameObject를 배치하고
/// 3D AudioSource를 추가한 뒤 SolarFacilityClearCutscene에 연결한다.
/// 이미 오브젝트가 있으면 위치/AudioSource 설정만 갱신한다.
/// </summary>
public static class SolarFacilityEnergyHumSetup
{
    private const string HumObjectName  = "SolarFacilityEnergyHum";
    private const string ManagerName    = "SolarFacilityClearCutsceneManager";

    // AudioSource 3D 설정
    private const float SpatialBlend   = 1f;
    private const float MinDistance    = 5f;
    private const float MaxDistance    = 40f;
    private const float DopplerLevel   = 0f;
    private const AudioRolloffMode RolloffMode = AudioRolloffMode.Linear;

    [MenuItem("Re_Bloom/Stage3/Setup Solar Facility Energy Hum")]
    private static void Setup()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            Debug.LogError("[EnergyHumSetup] 유효한 씬이 열려 있지 않습니다.");
            return;
        }

        // ── 1. SolarFacilityClearCutscene 찾기 ───────────────
        SolarFacilityClearCutscene manager =
            Object.FindFirstObjectByType<SolarFacilityClearCutscene>(
                FindObjectsInactive.Include);

        if (manager == null)
        {
            Debug.LogError(
                "[EnergyHumSetup] 씬에서 SolarFacilityClearCutscene을 찾지 못했습니다. " +
                "Re_Bloom/Stage3/Facility Clear Cutscene Setup - Apply를 먼저 실행하세요.");
            return;
        }

        // ── 2. 패널 중심 계산 ─────────────────────────────────
        SolarReflector[] reflectors = Object.FindObjectsByType<SolarReflector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (reflectors.Length == 0)
        {
            Debug.LogError(
                "[EnergyHumSetup] 씬에서 SolarReflector를 찾지 못했습니다. " +
                "Stage3.unity가 열려 있는지 확인하세요.");
            return;
        }

        Vector3 centroid = Vector3.zero;
        foreach (SolarReflector r in reflectors)
            centroid += r.transform.position;
        centroid /= reflectors.Length;

        Debug.Log(
            $"[EnergyHumSetup] 패널 {reflectors.Length}개 중심: {centroid:F2}");

        // ── 3. SolarFacilityEnergyHum GO 생성 또는 갱신 ───────
        GameObject existingGO = GameObject.Find(HumObjectName);
        bool isNew = existingGO == null;

        GameObject humGO;
        if (isNew)
        {
            humGO = new GameObject(HumObjectName);
            Undo.RegisterCreatedObjectUndo(humGO, $"Create {HumObjectName}");
            humGO.transform.position = centroid;
            Debug.Log($"[EnergyHumSetup] '{HumObjectName}' 신규 생성: {centroid:F2}");
        }
        else
        {
            humGO = existingGO;
            // 기존 오브젝트가 있으면 위치를 갱신하지 않음 — Inspector에서 직접 조절 가능
            Debug.Log($"[EnergyHumSetup] 기존 '{HumObjectName}' 사용 (위치 보존): {humGO.transform.position:F2}");
        }

        // ── 4. AudioSource 추가 또는 갱신 ────────────────────
        AudioSource audioSource = humGO.GetComponent<AudioSource>();
        bool audioSourceIsNew = audioSource == null;

        if (audioSourceIsNew)
        {
            audioSource = Undo.AddComponent<AudioSource>(humGO);
            Debug.Log($"[EnergyHumSetup] AudioSource 신규 추가.");
        }
        else
        {
            Undo.RecordObject(audioSource, "Update EnergyHum AudioSource");
            Debug.Log($"[EnergyHumSetup] 기존 AudioSource 갱신.");
        }

        // 3D 공간 음향 설정
        audioSource.playOnAwake    = false;
        audioSource.loop           = true;
        audioSource.spatialBlend   = SpatialBlend;
        audioSource.minDistance    = MinDistance;
        audioSource.maxDistance    = MaxDistance;
        audioSource.dopplerLevel   = DopplerLevel;
        audioSource.rolloffMode    = RolloffMode;

        // clip / volume: SolarFacilityClearCutscene에서 복사
        SerializedObject managerSO = new SerializedObject(manager);
        AudioClip humClip =
            managerSO.FindProperty("energyHumClip")?.objectReferenceValue as AudioClip;
        float humVolume =
            managerSO.FindProperty("energyHumVolume")?.floatValue ?? 0.4f;

        if (humClip != null)
        {
            audioSource.clip   = humClip;
            audioSource.volume = humVolume;
            Debug.Log(
                $"[EnergyHumSetup] clip={humClip.name}, volume={humVolume:F2} " +
                "(SolarFacilityClearCutscene에서 복사)");
        }
        else
        {
            Debug.LogWarning(
                "[EnergyHumSetup] SolarFacilityClearCutscene에 energyHumClip이 없습니다. " +
                "AudioSource.clip을 Inspector에서 직접 연결하세요.");
        }

        EditorUtility.SetDirty(audioSource);
        EditorUtility.SetDirty(humGO);

        // ── 5. SolarFacilityClearCutscene에 AudioSource 연결 ──
        managerSO.Update();
        SerializedProperty humSourceProp = managerSO.FindProperty("energyHumAudioSource");
        if (humSourceProp != null)
        {
            humSourceProp.objectReferenceValue = audioSource;
            managerSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            Debug.Log(
                $"[EnergyHumSetup] SolarFacilityClearCutscene.energyHumAudioSource → " +
                $"{humGO.name} 연결 완료.");
        }
        else
        {
            Debug.LogError(
                "[EnergyHumSetup] 'energyHumAudioSource' 프로퍼티를 찾지 못했습니다. " +
                "SolarFacilityClearCutscene.cs가 최신인지 확인하세요.");
        }

        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log(
            $"[EnergyHumSetup] 설정 완료!\n" +
            $"  ══════════ AudioSource 설정 ══════════\n" +
            $"  GameObject: '{HumObjectName}' {(isNew ? "(신규)" : "(기존 위치 보존)")}\n" +
            $"  위치: {humGO.transform.position:F2}\n" +
            $"  Spatial Blend: {SpatialBlend} (3D)\n" +
            $"  Min Distance: {MinDistance}m  Max Distance: {MaxDistance}m\n" +
            $"  Rolloff: {RolloffMode}  Doppler: {DopplerLevel}\n" +
            $"  Loop: true  Play On Awake: false\n" +
            $"  ══════════ 사용 방법 ══════════\n" +
            $"  - 위치 조정: Scene 뷰에서 '{HumObjectName}' Transform 직접 이동\n" +
            $"  - 볼륨/거리: Hierarchy에서 '{HumObjectName}' > AudioSource Inspector\n" +
            $"  - 씬 저장 후 테스트하세요.",
            humGO);

        Selection.activeGameObject = humGO;
    }
}
#endif
