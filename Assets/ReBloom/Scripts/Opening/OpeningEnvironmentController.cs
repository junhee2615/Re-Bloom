using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Opening 전용 Stage 환경 로더.
///
/// 책임은 넷뿐이다 — Additive Load/Unload / Stage 정리 / XR Rig 이동 / Lighting 전환.
/// 텍스트와 페이드 타이밍은 <see cref="OpeningNarrationController"/> 가 갖고,
/// 이 스크립트는 "검정인 동안 환경을 갈아끼우는" 일만 한다.
///
/// Stage 씬·프리팹·기존 게임 스크립트는 일절 수정하지 않는다.
/// 필요한 처리는 모두 런타임에 로드된 씬의 오브젝트를 끄는 방식으로만 한다.
///
/// Main Camera 태그를 잠시 떼는 이유:
/// Stage 쪽 컷씬 스크립트들은 Start()에서 `while (Camera.main == null) yield return null;`
/// 로 기다린 뒤 이름으로 "XR Origin"을 찾아 우리 리그의 참조를 잡는다. 로드되는 동안
/// Camera.main이 null이면 그 대기 루프를 벗어나지 못하므로, 참조를 잡기 전에 끌 수 있다.
/// Stage1에 2개(Stage1CinemachineBinder, Stage1XRCutsceneRigFollower),
/// Stage2에 3개(Mission1ClearCutscene, Stage2SkyCutscene, Stage2TeleporterCutscene)가 있다.
/// 화면이 검정인 구간에서만 바꾸므로 사용자에게는 보이지 않는다.
/// </summary>
public class OpeningEnvironmentController : MonoBehaviour
{
    /// <summary>Stage 하나를 Opening에서 어떻게 보여줄지에 대한 한 묶음.</summary>
    [Serializable]
    public class StageShot
    {
        [Tooltip("Additive로 올릴 씬 이름. Build Profiles > Scene List에 등록되어 있어야 한다.")]
        public string sceneName = "Stage1";

        [Tooltip("XR Origin을 놓을 월드 좌표.")]
        public Vector3 shotPosition;

        [Tooltip("XR Origin의 Yaw. VR이므로 Pitch/Roll은 적용하지 않는다.")]
        public float shotYaw;

        [Tooltip("이름이 완전히 일치하는 오브젝트만 비활성화한다. 루트와 자식 모두 탐색한다.")]
        public string[] disableObjectNames;

        [Tooltip("켜면 이 Stage에서 수로 장애물 시각 사본 처리를 수행한다. Stage2에서만 쓴다.")]
        public bool createPropFallbacks;
    }

    /// <summary>
    /// NetworkObject 때문에 보이지 않을 수 있는 오브젝트의 "시각 전용 대역".
    /// 원본이 비활성일 때만 이 모델을 같은 자리에 생성한다.
    /// </summary>
    [Serializable]
    public class PropFallback
    {
        [Tooltip("Stage 씬에서 찾을 원본 오브젝트 이름 (완전 일치).")]
        public string objectName;

        [Tooltip("대응하는 NetworkObject 없는 모델. Environment_Art/Props/Stage2/*_low.fbx")]
        public GameObject model;
    }

    [Header("Stage 촬영 데이터")]
    [Tooltip("[0] = Stage1, [1] = Stage2 순서로 둔다.")]
    [SerializeField] private StageShot[] stageShots;

    [Header("수로 장애물 시각 사본")]
    [Tooltip("원본이 비활성일 때만 생성한다. 활성이면 아무것도 만들지 않아 중복이 생기지 않는다.")]
    [SerializeField] private PropFallback[] propFallbacks;

    [Tooltip("생성한 사본을 모아 둘 Opening 전용 루트 이름.")]
    [SerializeField] private string propRootName = "OpeningStage2Props";

    [Header("자동 탐색 실패 시에만 연결 (보통 비워 둔다)")]
    [SerializeField] private Transform xrOriginOverride;
    [SerializeField] private Light openingDirectionalLightOverride;

    [Header("Debug")]
    [Tooltip("Camera.main이 준비될 때까지 기다릴 최대 프레임. 무한 대기를 막는 상한이다.")]
    [SerializeField, Min(1)] private int cameraResolveTimeoutFrames = 300;

    private const string MainCameraTag = "MainCamera";
    private const string UntaggedTag = "Untagged";
    private const string LocomotionRootName = "Locomotion";

    // 모두 1회만 해석한다. 매 프레임 탐색하지 않는다.
    private Camera rigCamera;
    private Transform xrOrigin;
    private Light openingDirectionalLight;
    private bool referencesResolved;

    // 현재 올라가 있는 Stage와, 그 Stage를 위해 만든 사본 루트.
    private int currentStageIndex = -1;
    private GameObject propRoot;

    // ── Locomotion 잠금 ─────────────────────────────────────────
    // 영속 XR Rig의 Locomotion GameObject. 초기화 시 1회만 찾는다.
    private GameObject locomotionRoot;
    private bool locomotionWasActive;
    private bool locomotionStateCaptured;

    // 연출 전 리그 포즈. Opening이 끝나면 이 자리로 되돌린다.
    private Transform cinematicRigRoot;
    private Vector3 rigOriginalPosition;
    private Quaternion rigOriginalRotation;
    private bool rigPoseCaptured;

    /// <summary>마지막 전환이 끝까지 성공했는지.</summary>
    public bool IsStageReady { get; private set; }

    /// <summary>
    /// 검정 상태에서 호출한다. 올라가 있던 Stage를 내리고 지정한 Stage로 갈아끼운 뒤
    /// 정리·조명 전환·리그 이동까지 끝낸다. 페이드는 호출한 쪽이 담당한다.
    /// </summary>
    public IEnumerator GoToStage(int index)
    {
        IsStageReady = false;

        if (stageShots == null || index < 0 || index >= stageShots.Length)
        {
            Debug.LogError($"[Opening] stageShots[{index}]가 없습니다.", this);
            yield break;
        }

        StageShot shot = stageShots[index];

        if (shot == null || string.IsNullOrWhiteSpace(shot.sceneName))
        {
            Debug.LogError($"[Opening] stageShots[{index}]의 씬 이름이 비어 있습니다.", this);
            yield break;
        }

        string sceneName = shot.sceneName.Trim();

        // ── 참조 해석 (최초 1회) ─────────────────────────────────
        // Camera.main은 태그를 떼기 전에 먼저 확보해야 한다.
        if (!referencesResolved)
        {
            yield return ResolveRigCamera();

            xrOrigin = xrOriginOverride != null ? xrOriginOverride : ResolveXROrigin();

            openingDirectionalLight = openingDirectionalLightOverride != null
                ? openingDirectionalLightOverride
                : ResolveOpeningDirectionalLight();

            referencesResolved = true;
        }

        // ── Opening의 Directional Light 비활성 ───────────────────
        // Stage가 자기 Directional Light(Stage1·Stage2 모두 Intensity 0.15)를 가져오므로
        // Opening 것을 남겨두면 빛이 두 개가 되어 어두운 톤이 깨진다.
        if (openingDirectionalLight != null)
            openingDirectionalLight.enabled = false;

        // ── Main Camera 태그 임시 해제 ───────────────────────────
        if (rigCamera != null)
            rigCamera.tag = UntaggedTag;

        // ── 이전 Stage 내리기 ────────────────────────────────────
        yield return UnloadCurrentStage();

        // ── Additive Load ───────────────────────────────────────
        Debug.Log($"[Opening] {sceneName} additive load start");

        Scene stageScene = SceneManager.GetSceneByName(sceneName);

        if (!stageScene.isLoaded)
        {
            AsyncOperation load =
                SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (load == null)
            {
                Debug.LogError(
                    $"[Opening] 씬 '{sceneName}'을 로드할 수 없습니다. " +
                    "File > Build Profiles > Scene List에 등록되어 있는지 확인하세요.", this);

                RestoreAfterFailure();
                yield break;
            }

            yield return load;

            stageScene = SceneManager.GetSceneByName(sceneName);
        }

        currentStageIndex = index;

        Debug.Log($"[Opening] {sceneName} loaded");

        // ── 불필요한 오브젝트만 비활성화 ─────────────────────────
        DisableUnwantedObjects(stageScene, shot.disableObjectNames);

        Debug.Log($"[Opening] {sceneName} cleanup complete");

        // ── 수로 장애물 시각 사본 (필요한 것만) ──────────────────
        if (shot.createPropFallbacks)
        {
            int created = CreatePropFallbacks(stageScene);
            Debug.Log($"[Opening] {sceneName} fallback props: {created}");
        }

        // ── Active Scene 전환 ───────────────────────────────────
        // Unity는 Active Scene의 RenderSettings(Skybox / Ambient / Fog)를 사용한다.
        // 이 호출 하나로 Stage의 조명 환경이 그대로 적용된다.
        if (stageScene.IsValid() && stageScene.isLoaded)
            SceneManager.SetActiveScene(stageScene);

        // ── XR Rig 이동 ─────────────────────────────────────────
        MoveRigToShot(shot);

        Debug.Log($"[Opening] Rig moved to {sceneName}");

        // ── Main Camera 태그 복구 ───────────────────────────────
        if (rigCamera != null)
            rigCamera.tag = MainCameraTag;

        IsStageReady = true;

        Debug.Log($"[Opening] {sceneName} ready");
    }

    /// <summary>
    /// 검정 상태에서 호출한다. 올라가 있는 Stage를 내리고 다음 Stage는 올리지 않는다.
    /// 전환용 <see cref="GoToStage"/> 와 같은 언로드 경로를 그대로 재사용한다.
    ///
    /// Opening의 비영속 XR Rig와 ScreenFadeCanvas / OpeningText는 Opening 씬 소유이므로
    /// Stage 언로드와 무관하게 그대로 남는다.
    /// </summary>
    public IEnumerator UnloadStage()
    {
        IsStageReady = false;

        yield return UnloadCurrentStage();
    }

    /// <summary>
    /// 올라가 있는 Stage를 내린다. 아무것도 없으면 즉시 반환한다.
    ///
    /// Active Scene인 씬을 그대로 언로드하면 Active가 불확정해지므로,
    /// 먼저 Active를 Opening으로 되돌린 뒤 언로드한다.
    /// </summary>
    private IEnumerator UnloadCurrentStage()
    {
        if (currentStageIndex < 0)
            yield break;

        StageShot previous = stageShots[currentStageIndex];
        string previousName = previous != null ? previous.sceneName.Trim() : null;

        currentStageIndex = -1;

        if (string.IsNullOrEmpty(previousName))
            yield break;

        // 이전 Stage를 위해 만든 시각 사본을 먼저 치운다.
        if (propRoot != null)
        {
            Destroy(propRoot);
            propRoot = null;
        }

        Scene previousScene = SceneManager.GetSceneByName(previousName);

        if (!previousScene.IsValid() || !previousScene.isLoaded)
            yield break;

        // Active를 Opening으로 되돌린다.
        Scene openingScene = gameObject.scene;

        if (openingScene.IsValid() && openingScene.isLoaded)
            SceneManager.SetActiveScene(openingScene);

        AsyncOperation unload = SceneManager.UnloadSceneAsync(previousScene);

        if (unload != null)
            yield return unload;

        // Quest 메모리를 위해 쓰지 않는 에셋을 돌려준다.
        yield return Resources.UnloadUnusedAssets();

        Debug.Log($"[Opening] {previousName} unloaded");
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
    private void MoveRigToShot(StageShot shot)
    {
        if (xrOrigin == null)
        {
            Debug.LogError(
                "[Opening] XR Origin을 찾지 못해 리그를 옮기지 못했습니다.", this);
            return;
        }

        Vector3 target = shot.shotPosition;

        if (rigCamera != null)
        {
            Vector3 headOffset = rigCamera.transform.position - xrOrigin.position;
            headOffset.y = 0f;   // 수평 보정만. 높이는 건드리지 않는다.
            target -= headOffset;
        }

        xrOrigin.SetPositionAndRotation(target, Quaternion.Euler(0f, shot.shotYaw, 0f));
    }

    /// <summary>
    /// 이름이 완전히 일치하는 오브젝트만 비활성화한다. 루트와 자식을 모두 본다.
    /// 추측으로 끄지 않도록 부분 일치는 쓰지 않고, 못 찾은 이름은 로그로 알린다.
    /// </summary>
    private void DisableUnwantedObjects(Scene scene, string[] names)
    {
        if (!scene.IsValid() || !scene.isLoaded || names == null)
            return;

        var targets = new HashSet<string>();

        foreach (string name in names)
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

    /// <summary>
    /// 수로를 막는 장애물이 Runner 없는 Opening에서 보이지 않을 때를 위한 안전장치.
    ///
    /// Soil / Stone / Wood 계열은 루트에 Fusion NetworkObject를 갖고 있어
    /// Runner가 없으면 비활성 상태로 남을 수 있다. 그런데 보이는 메시는
    /// 중첩된 *_low.fbx 모델이 갖고 있고 그 모델에는 Fusion 컴포넌트가 없다.
    /// 그래서 원본의 Transform만 읽어 모델을 같은 자리에 하나 더 세운다.
    ///
    /// 원본이 이미 활성이면 아무것도 만들지 않으므로, Fusion이 어떻게 동작하든
    /// 결과가 중복되지 않는다. 원본·프리팹·Fusion 컴포넌트는 읽기만 한다.
    /// 사본은 MeshFilter/MeshRenderer만 가진 순수 비주얼이다
    /// (대상 FBX는 addColliders: 0 이라 Collider도 생기지 않는다).
    /// </summary>
    private int CreatePropFallbacks(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded || propFallbacks == null)
            return 0;

        var models = new Dictionary<string, GameObject>();

        foreach (PropFallback fallback in propFallbacks)
        {
            if (fallback == null || string.IsNullOrWhiteSpace(fallback.objectName))
                continue;

            if (fallback.model == null)
            {
                Debug.LogWarning(
                    $"[Opening] '{fallback.objectName}'의 대역 모델이 연결되지 않았습니다.", this);
                continue;
            }

            models[fallback.objectName.Trim()] = fallback.model;
        }

        if (models.Count == 0)
            return 0;

        int created = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform original in root.GetComponentsInChildren<Transform>(true))
            {
                if (!models.TryGetValue(original.name, out GameObject model))
                    continue;

                // 이미 보이는 원본은 건드리지 않는다. 사본을 만들면 겹친다.
                if (original.gameObject.activeInHierarchy)
                    continue;

                EnsurePropRoot();

                GameObject copy = Instantiate(
                    model, original.position, original.rotation, propRoot.transform);

                copy.transform.localScale = original.lossyScale;
                copy.name = $"{original.name}_OpeningVisual";

                created++;
            }
        }

        return created;
    }

    /// <summary>사본을 담을 루트를 Opening 씬에 만든다. Stage를 떠날 때 통째로 지운다.</summary>
    private void EnsurePropRoot()
    {
        if (propRoot != null)
            return;

        propRoot = new GameObject(
            string.IsNullOrWhiteSpace(propRootName) ? "OpeningStage2Props" : propRootName);

        Scene openingScene = gameObject.scene;

        if (openingScene.IsValid() && openingScene.isLoaded)
            SceneManager.MoveGameObjectToScene(propRoot, openingScene);
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
    /// 런타임 XR Origin을 찾는다.
    ///
    /// Opening 씬에는 더 이상 VRSystem이 없고, 리그는 StartScene에서
    /// DontDestroyOnLoad로 넘어온다. 그래서 Opening 씬 범위로는 찾을 수 없다.
    ///
    /// 1차: 프로젝트의 기존 컷씬 스크립트들과 같은 방식으로 Main Camera에서
    ///      부모를 거슬러 올라간다. Camera.main은 DontDestroyOnLoad 영역도 본다.
    /// 2차: HardwareRig가 XR Origin 루트에 붙어 있으므로 그 타입으로 전역 탐색한다.
    ///      (위치 파악에만 쓰고 HardwareRig의 메서드는 호출하지 않는다.)
    ///
    /// 둘 다 1회만 수행한다. 매 프레임 탐색하지 않는다.
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

        HardwareRig rig = FindAnyObjectByType<HardwareRig>(FindObjectsInactive.Include);

        return rig != null ? rig.transform : null;
    }

    /// <summary>
    /// Opening 씬 루트에서 Directional Light를 찾는다.
    ///
    /// XR Origin과 달리 이쪽은 **의도적으로 Opening 씬 범위로 한정한다.**
    /// 전역 탐색을 하면 Additive로 올라온 Stage의 Directional Light나
    /// StartScene에 남아 있는 조명을 잡아 엉뚱한 빛을 끄게 된다.
    /// Opening의 Directional Light는 Opening 씬의 루트 오브젝트다.
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

    /// <summary>
    /// Opening이 시작되는 가장 이른 시점에 영속 XR Rig의 Locomotion을 잠근다.
    ///
    /// Awake에서 하는 이유:
    /// 검정 내레이션은 Stage가 올라오기 전부터 약 15초 이어진다. 그 구간의 Opening에는
    /// 콜라이더가 하나도 없으므로 GravityProvider가 접지하지 못하고 종단속도(90m/s)까지
    /// 가속한다. GravityProvider의 낙하 속도는 접지될 때만 초기화되고 Transform을 직접
    /// 옮기는 것으로는 리셋되지 않으므로, 뒤늦게 GoToStage에서 잠그면 이미 쌓인 속도 때문에
    /// 리그가 촬영 위치에 놓인 직후 그대로 지면까지 떨어진다.
    /// 그래서 Stage 로드 시점이 아니라 이 컴포넌트가 깨어나는 즉시 잠근다.
    /// </summary>
    private void Awake()
    {
        LockLocomotionForCinematic();
    }

    /// <summary>
    /// Opening이 어떤 경로로 끝나더라도(정상 종료 / 코루틴 중단 / 예외 / 씬 전환)
    /// 영속 리그에 꺼진 Locomotion이 남지 않도록 여기서 되돌린다.
    /// 코루틴 끝에서만 복구하면 예외로 코루틴이 조용히 멈출 때 영구히 꺼진 채 남는다.
    /// </summary>
    private void OnDestroy()
    {
        RestoreLocomotion("[Opening] Locomotion restored");
    }

    /// <summary>
    /// Locomotion GameObject 전체를 비활성화한다.
    ///
    /// GravityProvider만 끄지 않고 GameObject를 끄는 이유는 복구할 상태가 bool 하나로
    /// 끝나기 때문이다. 프로바이더를 개별로 끄면 Turn/Grab처럼 원래부터 꺼져 있던
    /// 대상의 상태를 따로 기억해야 하고, 복구 누락이 조용히 남는다.
    ///
    /// InputActionManager는 XR Origin **루트**에 붙어 있고 Locomotion의 자식
    /// (Turn/Move/Grab/Teleportation/Climb/Gravity/Jump)에는 없다. 따라서 이 호출은
    /// InputActionManager.OnDisable을 트리거하지 않으며, 공유 XRI Input Actions가
    /// 전역으로 Disable되는 사고는 일어나지 않는다.
    /// XR Origin 자체 / Main Camera / Controller / PhysicsHands는 건드리지 않으므로
    /// HMD 트래킹과 손 트래킹은 그대로 동작한다.
    /// </summary>
    private void LockLocomotionForCinematic()
    {
        if (locomotionStateCaptured)
            return;

        Transform rigRoot = xrOriginOverride != null ? xrOriginOverride : ResolveRigRootEarly();

        if (rigRoot == null)
        {
            Debug.LogWarning(
                "[Opening] XR Origin을 찾지 못해 Locomotion을 잠그지 못했습니다. " +
                "StartScene의 영속 VRSystem이 살아 있는지 확인하세요.", this);
            return;
        }

        // 연출로 리그를 옮기기 전의 포즈를 여기서 한 번만 찍어 둔다.
        cinematicRigRoot = rigRoot;
        rigOriginalPosition = rigRoot.position;
        rigOriginalRotation = rigRoot.rotation;
        rigPoseCaptured = true;

        Transform found = FindLocomotionRoot(rigRoot);

        if (found == null)
        {
            Debug.LogWarning(
                $"[Opening] '{rigRoot.name}' 아래에서 'Locomotion'을 찾지 못했습니다. " +
                "XR Origin 프리팹의 이름이 바뀌었는지 확인하세요.", this);
            return;
        }

        // 원래 상태를 먼저 기억한다. 복구는 무조건 true가 아니라 이 값으로 되돌린다.
        locomotionRoot = found.gameObject;
        locomotionWasActive = locomotionRoot.activeSelf;
        locomotionStateCaptured = true;

        if (!locomotionWasActive)
            return;   // 이미 꺼져 있었다면 건드릴 것도, 로그를 남길 것도 없다.

        locomotionRoot.SetActive(false);

        Debug.Log("[Opening] Locomotion disabled for cinematic");
    }

    /// <summary>
    /// 저장해 둔 원래 activeSelf 값으로 되돌리고 참조·플래그를 정리한다.
    /// locomotionStateCaptured 가드 때문에 몇 번 불러도 안전하다.
    /// </summary>
    private void RestoreLocomotion(string logMessage)
    {
        if (!locomotionStateCaptured)
            return;

        bool changed = false;

        // 리그는 영속이므로 Opening이 사라질 때도 보통 살아 있다.
        // 애플리케이션 종료 등으로 먼저 파괴된 경우에만 null이 된다.
        if (locomotionRoot != null && locomotionRoot.activeSelf != locomotionWasActive)
        {
            locomotionRoot.SetActive(locomotionWasActive);
            changed = true;
        }

        locomotionRoot = null;
        locomotionWasActive = false;
        locomotionStateCaptured = false;

        if (changed)
            Debug.Log(logMessage);
    }


    /// <summary>
    /// Opening 정상 종료 경로에서 호출하는 명시적 Locomotion 복구.
    ///
    /// <see cref="OnDestroy"/> 복구는 안전장치로 그대로 남아 있고, 이 메서드와 같은
    /// 구현(<c>RestoreLocomotion</c>)을 공유한다. locomotionStateCaptured 가드 덕분에
    /// 둘 중 어느 쪽이 먼저 불려도, 두 번 불려도 안전하다.
    ///
    /// 호출 시점 주의:
    /// Opening 씬에는 콜라이더가 하나도 없다. 그래서 Lobby가 올라오기 **전에** 여기를
    /// 부르면 중력이 다시 켜져 세션 접속을 기다리는 수 초 동안 리그가 다시 추락한다.
    /// 반드시 Lobby 로드가 끝난 뒤(또는 Opening이 파괴되는 순간)에 복구해야 한다.
    /// </summary>
    public void RestoreLocomotionForExit()
    {
        RestoreLocomotion("[Opening] Locomotion restored for exit");
    }

    /// <summary>
    /// 리그를 Opening 시작 전(StartScene) 자리로 되돌린다.
    ///
    /// Opening은 연출을 위해 리그를 Stage1·Stage2의 촬영 좌표로 옮겨 놓는다.
    /// 그런데 Lobby에는 리그를 다시 배치하는 코드가 없다
    /// (PlayerSpawner의 noSpawnScenes에 Lobby가 들어 있고, HardwareRig.TeleportTo를
    /// 부르는 곳도 없다). 그대로 두면 플레이어가 Stage2의 촬영 좌표
    /// (-5, 2.5, 7) 그대로 Lobby에 도착한다.
    ///
    /// 그래서 Awake에서 찍어 둔 원래 포즈로 되돌려, Opening을 거치지 않았던
    /// 기존 StartScene → Lobby 흐름과 같은 상태로 세션에 들어간다.
    /// 몇 번 불려도 결과가 같다.
    /// </summary>
    public void RestoreRigPoseForExit()
    {
        if (!rigPoseCaptured || cinematicRigRoot == null)
            return;

        cinematicRigRoot.SetPositionAndRotation(rigOriginalPosition, rigOriginalRotation);

        Debug.Log("[Opening] Rig pose restored for exit");
    }
    /// <summary>
    /// Awake 시점에 영속 XR Origin 루트를 찾는다.
    ///
    /// <see cref="ResolveXROrigin"/> 는 이미 해석해 둔 <see cref="rigCamera"/> 에 의존하는데
    /// 그 해석은 Stage 로드 시점의 코루틴에서 일어난다. Locomotion 잠금은 그보다 앞서야
    /// 하므로 여기서는 카메라 상태에 의존하지 않는 경로를 먼저 쓴다.
    ///
    /// 1차: HardwareRig. XR Origin 루트에 붙어 있어 가장 확정적이다.
    ///      (위치 파악에만 쓰고 HardwareRig의 메서드는 호출하지 않는다.)
    /// 2차: Camera.main에서 부모를 거슬러 올라간다. 리그는 StartScene에서
    ///      DontDestroyOnLoad로 넘어오므로 Awake 시점에도 이미 살아 있다.
    /// </summary>
    private Transform ResolveRigRootEarly()
    {
        HardwareRig rig = FindAnyObjectByType<HardwareRig>(FindObjectsInactive.Include);

        if (rig != null)
            return rig.transform;

        Camera cam = Camera.main;

        if (cam != null)
        {
            Transform current = cam.transform;

            while (current != null)
            {
                if (current.name.Contains("XR Origin"))
                    return current;

                current = current.parent;
            }
        }

        return null;
    }

    /// <summary>
    /// 리그 아래에서 이름이 정확히 "Locomotion"인 GameObject를 찾는다.
    /// 추측으로 끄지 않도록 부분 일치는 쓰지 않는다.
    /// 비활성 상태일 수도 있으므로 includeInactive = true 로 순회한다.
    /// </summary>
    private Transform FindLocomotionRoot(Transform rigRoot)
    {
        foreach (Transform t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t != rigRoot && t.name == LocomotionRootName)
                return t;
        }

        return null;
    }
}
