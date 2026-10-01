using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Opening 전용 Stage 환경 로더.
///
/// 책임은 넷뿐이다 — Additive Load / Stage 정리 / XR Rig 이동 / Lighting 전환.
/// 텍스트와 페이드 타이밍은 <see cref="OpeningNarrationController"/> 가 갖고,
/// 이 스크립트는 "검정인 동안 환경을 갈아끼우는" 일만 한다.
///
/// Stage 씬·프리팹·기존 게임 스크립트는 일절 수정하지 않는다.
/// 필요한 처리는 모두 런타임에 로드된 씬의 오브젝트를 끄는 방식으로만 한다.
///
/// Main Camera 태그를 잠시 떼는 이유:
/// Stage 쪽 컷씬 스크립트들(Stage1CinemachineBinder, Stage1XRCutsceneRigFollower 등)은
/// Start()에서 `while (Camera.main == null) yield return null;` 로 기다린 뒤
/// 이름으로 "XR Origin"을 찾아 우리 리그의 참조를 잡는다. 로드되는 동안 Camera.main이
/// null이면 그 대기 루프를 벗어나지 못하므로, 참조를 잡기 전에 해당 오브젝트를 끌 수 있다.
/// 화면이 검정인 구간에서만 바꾸므로 사용자에게는 보이지 않는다.
/// </summary>
public class OpeningEnvironmentController : MonoBehaviour
{
    [Header("Stage")]
    [Tooltip("Additive로 올릴 씬 이름. Build Profiles > Scene List에 등록되어 있어야 한다.")]
    [SerializeField] private string stageSceneName = "Stage1";

    [Header("Opening 전용 Stage1 촬영 위치")]
    [Tooltip("폐허 링의 중심 상공. 링 중심은 (-7.19, ?, -0.88), 평균 반경 35.6m다. " +
             "여기 서면 어느 방향을 봐도 폐허가 이어진다. 높이만 바꿔 부감 정도를 조절한다.")]
    [SerializeField] private Vector3 stage1ShotPosition = new Vector3(-7f, 25f, -1f);

    [Tooltip("XR Origin의 Yaw. VR이므로 Pitch/Roll은 적용하지 않는다.")]
    [SerializeField] private float stage1ShotYaw = 0f;

    [Header("Opening에 불필요해서 끌 Stage 오브젝트")]
    [Tooltip("이름이 완전히 일치하는 오브젝트만 비활성화한다. 루트와 자식 모두 탐색한다. " +
             "실제 Stage1 Hierarchy에서 존재를 확인한 것만 넣었다.")]
    [SerializeField]
    private string[] disableObjectNames =
    {
        // 루트 — Opening의 EventSystem과 중복되거나 미션/컷씬 로직을 들고 있다
        "EventSystem",
        "TutorialMissionManager",
        "Stage1ClearCutscene_Setup",
        "Stage1ClearVideoPlayer",
        "PartnerDirectionIndicatorBootstrap",
        "MissionOutlineHighlighter",
        "TestPartnerTarget",
        "--- DEBUG (Test Only) ---",
        "ResonanceSceneContext",

        // Stage1ClearCutscene_Setup의 자식 — 부모를 끄면 함께 꺼지지만 명시해 둔다
        "Stage1ClearDirector",
        "Stage1CutsceneCamera",

        // 그 밖의 연출 오브젝트
        "TrainDepartureAudio",

        // Objects 아래의 미션 프리팹
        "FuelGenerator",
        "GeneratorPart",
        "PartTroubleGenerator",
        "Fuel",
        "ValveMissionPrefab",
    };

    [Header("자동 탐색 실패 시에만 연결 (보통 비워 둔다)")]
    [SerializeField] private Transform xrOriginOverride;
    [SerializeField] private Light openingDirectionalLightOverride;

    [Header("Debug")]
    [Tooltip("Camera.main이 준비될 때까지 기다릴 최대 프레임. 무한 대기를 막는 상한이다.")]
    [SerializeField, Min(1)] private int cameraResolveTimeoutFrames = 300;

    private const string MainCameraTag = "MainCamera";
    private const string UntaggedTag = "Untagged";

    // 모두 1회만 해석한다. 매 프레임 탐색하지 않는다.
    private Camera rigCamera;
    private Transform xrOrigin;
    private Light openingDirectionalLight;

    /// <summary>Stage 로드와 배치가 끝났는지. 실패하면 false로 남는다.</summary>
    public bool IsStageReady { get; private set; }

    /// <summary>
    /// 검정 상태에서 호출한다. Stage를 Additive로 올리고 정리·조명 전환·리그 이동까지 끝낸다.
    /// 페이드는 호출한 쪽이 담당한다.
    /// </summary>
    public IEnumerator LoadStageBlind()
    {
        // ── 참조 해석 (1회) ──────────────────────────────────────
        // Camera.main은 태그를 떼기 전에 먼저 확보해야 한다.
        yield return ResolveRigCamera();

        xrOrigin = xrOriginOverride != null ? xrOriginOverride : ResolveXROrigin();

        openingDirectionalLight = openingDirectionalLightOverride != null
            ? openingDirectionalLightOverride
            : ResolveOpeningDirectionalLight();

        // ── Opening의 Directional Light 비활성 ───────────────────
        // Stage1이 자기 Directional Light(Intensity 0.15)를 함께 가져오므로
        // Opening 것을 남겨두면 빛이 두 개가 되어 Stage1의 어두운 톤이 깨진다.
        if (openingDirectionalLight != null)
            openingDirectionalLight.enabled = false;

        // ── Main Camera 태그 임시 해제 ───────────────────────────
        if (rigCamera != null)
            rigCamera.tag = UntaggedTag;

        // ── Additive Load ───────────────────────────────────────
        Debug.Log($"[Opening] {stageSceneName} additive load start");

        Scene stageScene = SceneManager.GetSceneByName(stageSceneName);

        if (!stageScene.isLoaded)
        {
            AsyncOperation load =
                SceneManager.LoadSceneAsync(stageSceneName, LoadSceneMode.Additive);

            if (load == null)
            {
                Debug.LogError(
                    $"[Opening] 씬 '{stageSceneName}'을 로드할 수 없습니다. " +
                    "File > Build Profiles > Scene List에 등록되어 있는지 확인하세요.", this);

                RestoreAfterFailure();
                yield break;
            }

            yield return load;

            stageScene = SceneManager.GetSceneByName(stageSceneName);
        }

        Debug.Log($"[Opening] {stageSceneName} loaded");

        // ── 불필요한 오브젝트만 비활성화 ─────────────────────────
        DisableUnwantedObjects(stageScene);

        Debug.Log($"[Opening] {stageSceneName} cleanup complete");

        // ── Active Scene 전환 ───────────────────────────────────
        // Unity는 Active Scene의 RenderSettings(Skybox / Ambient / Fog)를 사용한다.
        // 이 호출 하나로 Stage1의 조명 환경이 그대로 적용된다.
        if (stageScene.IsValid() && stageScene.isLoaded)
            SceneManager.SetActiveScene(stageScene);

        // ── XR Rig 이동 ─────────────────────────────────────────
        MoveRigToShotPosition();

        Debug.Log($"[Opening] Rig moved to {stageSceneName}");

        // ── Main Camera 태그 복구 ───────────────────────────────
        if (rigCamera != null)
            rigCamera.tag = MainCameraTag;

        IsStageReady = true;

        Debug.Log($"[Opening] {stageSceneName} ready");
    }

    /// <summary>
    /// Opening 전용 촬영 위치로 리그를 놓는다.
    ///
    /// 머리 오프셋 보정은 게임이 쓰는 규약(HardwareRig.TeleportTo)과 같다 —
    /// 플레이 공간 안에서 플레이어가 어디에 서 있든 머리의 '수평' 위치가
    /// 촬영 기준점에 오도록 리그를 역보정하고, 리그 y는 기준 y를 그대로 쓴다.
    /// headOffset.y를 0으로 두므로 HMD 높이는 트래킹에서 오는 값이 그대로 유지된다.
    ///
    /// 회전은 Yaw만 적용한다. Pitch/Roll을 강제하면 VR에서 지평선이 기울어
    /// 멀미를 유발하므로 상하 시선은 전적으로 HMD에 맡긴다.
    /// </summary>
    private void MoveRigToShotPosition()
    {
        if (xrOrigin == null)
        {
            Debug.LogError(
                "[Opening] XR Origin을 찾지 못해 리그를 옮기지 못했습니다.", this);
            return;
        }

        Vector3 target = stage1ShotPosition;

        if (rigCamera != null)
        {
            Vector3 headOffset = rigCamera.transform.position - xrOrigin.position;
            headOffset.y = 0f;   // 수평 보정만. 높이는 건드리지 않는다.
            target -= headOffset;
        }

        xrOrigin.SetPositionAndRotation(target, Quaternion.Euler(0f, stage1ShotYaw, 0f));
    }

    /// <summary>
    /// 이름이 완전히 일치하는 오브젝트만 비활성화한다. 루트와 자식을 모두 본다.
    /// 추측으로 끄지 않도록 부분 일치는 쓰지 않고, 못 찾은 이름은 로그로 알린다.
    /// </summary>
    private void DisableUnwantedObjects(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || disableObjectNames == null)
            return;

        var targets = new HashSet<string>();

        foreach (string name in disableObjectNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
                targets.Add(name.Trim());
        }

        if (targets.Count == 0)
            return;

        var found = new HashSet<string>();

        // 비활성 오브젝트까지 보도록 includeInactive = true. 씬 전체를 한 번만 순회한다.
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!targets.Contains(t.name))
                    continue;

                t.gameObject.SetActive(false);
                found.Add(t.name);
            }
        }

        foreach (string name in targets)
        {
            if (!found.Contains(name))
            {
                Debug.LogWarning(
                    $"[Opening] '{scene.name}'에서 '{name}'을 찾지 못했습니다. " +
                    "이름이 바뀌었는지 확인하세요.", this);
            }
        }
    }

    /// <summary>Camera.main이 준비될 때까지 프레임 상한을 두고 기다린다.</summary>
    private IEnumerator ResolveRigCamera()
    {
        for (int frame = 0; frame < cameraResolveTimeoutFrames; frame++)
        {
            rigCamera = Camera.main;

            if (rigCamera != null)
                yield break;

            yield return null;
        }

        Debug.LogWarning(
            "[Opening] Camera.main을 찾지 못했습니다. Main Camera 태그 처리를 건너뜁니다.", this);
    }

    /// <summary>
    /// 런타임 XR Origin을 찾는다. 프로젝트의 기존 컷씬 스크립트들과 같은 방식으로
    /// Main Camera에서 부모를 거슬러 올라가고, 실패하면 Opening 씬 루트에서 이름으로 찾는다.
    /// </summary>
    private Transform ResolveXROrigin()
    {
        if (rigCamera != null)
        {
            Transform current = rigCamera.transform;

            while (current != null)
            {
                if (current.name.Contains("XR Origin"))
                    return current;

                current = current.parent;
            }
        }

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Contains("XR Origin"))
                    return t;
            }
        }

        return null;
    }

    /// <summary>
    /// Opening 씬 루트에서 Directional Light를 찾는다.
    /// 탐색 범위를 이 스크립트가 속한 씬으로 한정하므로 Stage의 조명을 잡을 일이 없다.
    /// </summary>
    private Light ResolveOpeningDirectionalLight()
    {
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            Light light = root.GetComponent<Light>();

            if (light != null && light.type == LightType.Directional)
                return light;
        }

        return null;
    }

    /// <summary>로드에 실패했을 때 바꿔 둔 상태를 되돌린다.</summary>
    private void RestoreAfterFailure()
    {
        if (rigCamera != null)
            rigCamera.tag = MainCameraTag;

        if (openingDirectionalLight != null)
            openingDirectionalLight.enabled = true;
    }
}
