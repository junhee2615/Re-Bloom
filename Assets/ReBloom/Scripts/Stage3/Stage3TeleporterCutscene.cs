using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Stage 3 태양광 시설 클리어 후 텔레포터를 안내하는 로컬 컷씬.
/// 미션 상태와 텔레포터 잠금 상태는 변경하지 않는다.
/// </summary>
[AddComponentMenu("ReBloom/Stage 3 Teleporter Cutscene")]
public class Stage3TeleporterCutscene : MonoBehaviour
{
    [Header("텔레포터 컷")]
    [Tooltip("텔레포터를 보여주는 XR Origin 촬영 위치와 회전.")]
    [SerializeField] private Transform teleporterCutsceneShot;

    [Header("텔레포터 VFX")]
    [SerializeField] private GameObject[] teleporterVfxObjects = Array.Empty<GameObject>();

    [Header("텔레포터 효과음")]
    [SerializeField] private AudioClip teleporterActivateClip;
    [Range(0f, 1f)]
    [SerializeField] private float teleporterActivateVolume = 0.6f;
    [SerializeField] private AudioSource effectSource;

    [Header("카메라 / 페이드")]
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private float cameraMoveDistance = 2f;
    [SerializeField] private float cameraMoveDuration = 4f;
    [SerializeField] private float viewHoldDuration = 1f;
    [SerializeField] private bool smoothCameraMovement = true;
    [SerializeField] private bool followYaw = true;
    [SerializeField] private bool followPitch;

    [Header("연출 중 화면 정리")]
    [SerializeField] private bool disableResonance = true;
    [SerializeField] private bool disableBuiltinFog = true;
    [SerializeField] private bool hideRemoteAvatars = true;
    [SerializeField] private bool lockLocomotion = true;
    [SerializeField] private bool hideTutorialCanvas = true;

    [Header("Debug")]
    [SerializeField] private bool xrReady;
    [SerializeField] private bool isPlaying;

    public static event Action CutsceneFinished;

    private Camera runtimeCamera;
    private Transform xrHead;
    private Transform xrOrigin;
    private ScreenFade screenFade;
    private HardwareRig hardwareRig;
    private bool pendingPlay;
    private bool previewPlayback;
    private bool[] previewVfxInitialStates;

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;

    private Vector3 currentShotPosition;
    private Quaternion currentShotRotation;
    private bool hasCurrentShot;

    private readonly ResonanceCutsceneOverride resonance =
        new ResonanceCutsceneOverride();

    public bool IsPlaying => isPlaying;
    public bool CanPlay => xrReady && !isPlaying;

    private void OnEnable()
    {
        // 외부 컷씬에서 TryPlay()로 호출한다.
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        CleanupAfterCutscene();
    }

    private void OnDestroy()
    {
        if (effectSource != null)
            effectSource.Stop();
    }

    private IEnumerator Start()
    {
        while (runtimeCamera == null)
        {
            runtimeCamera = Camera.main;
            yield return null;
        }

        xrHead = runtimeCamera.transform;

        Transform current = xrHead;
        while (current != null)
        {
            if (current.name.Contains("XR Origin"))
            {
                xrOrigin = current;
                break;
            }

            current = current.parent;
        }

        if (xrOrigin == null)
        {
            Debug.LogError("[Stage3TeleporterCutscene] XR Origin을 찾지 못했습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        ResolveTeleporterVfxObjects();
        xrReady = true;

        if (pendingPlay)
        {
            pendingPlay = false;
            TryPlay(previewPlayback);
        }
    }

    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }

    /// <summary>외부 컷씬 체인이나 Stage 3 미션 로직에서 호출한다.</summary>
    public bool TryPlay()
    {
        return TryPlay(false);
    }

    private bool TryPlay(bool preview)
    {
        if (isPlaying)
            return false;

        if (!xrReady)
        {
            previewPlayback = preview;
            pendingPlay = true;
            return false;
        }

        previewPlayback = preview;
        StartCoroutine(PlayRoutine(preview));
        return true;
    }

    [ContextMenu("Play Teleporter Cutscene")]
    private void ContextPlayCutscene()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Stage3TeleporterCutscene] Play 모드에서만 실행할 수 있습니다.");
            return;
        }

        TryPlay(true);
    }

    /// <summary>Inspector 미리보기용 진입점. 미션 상태와 텔레포터 잠금 상태는 변경하지 않는다.</summary>
    public void EditorTestPlay()
    {
        if (!Application.isPlaying)
            return;

        TryPlay(true);
    }

    private IEnumerator PlayRoutine(bool preview)
    {
        isPlaying = true;
        previewVfxInitialStates = preview
            ? CaptureVfxInitialStates()
            : null;
        SaveOriginalXRTransform();

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);

        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        yield return FadeOut();

        if (disableResonance || disableBuiltinFog)
            resonance.Disable(disableBuiltinFog);

        yield return PlayTeleporterShot();

        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();

        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        yield return null;
        yield return FadeIn();

        isPlaying = false;

        Debug.Log("[Stage3TeleporterCutscene] 텔레포터 컷씬 종료", this);

        if (preview)
            RestorePreviewVfxStates();

        previewPlayback = false;
        CutsceneFinished?.Invoke();
    }

    /// <summary>
    /// 시설 복구 컷씬이 페이드아웃과 XR 복원을 끝낸 뒤 호출되는 연속 재생 루틴.
    /// 안개/시야 제한과 화면 복원 소유권은 시설 컷씬이 유지한다.
    /// </summary>
    internal IEnumerator PlayInSequenceRoutine()
    {
        if (isPlaying)
            yield break;

        isPlaying = true;
        SaveOriginalXRTransform();

        yield return PlayTeleporterShot();

        hasCurrentShot = false;
        RestoreOriginalXRTransform();

        yield return null;
        yield return FadeIn();

        isPlaying = false;
        CutsceneFinished?.Invoke();
    }

    private IEnumerator PlayTeleporterShot()
    {
        if (teleporterCutsceneShot == null)
        {
            Debug.LogWarning(
                "[Stage3TeleporterCutscene] Teleporter Cutscene Shot이 설정되지 않았습니다.",
                this);

            ActivateTeleporter();
            yield return FadeIn();

            if (viewHoldDuration > 0f)
                yield return new WaitForSeconds(viewHoldDuration);

            yield return FadeOut();
            yield break;
        }

        Vector3 startPosition = teleporterCutsceneShot.position;
        Quaternion startRotation = teleporterCutsceneShot.rotation;
        SetCurrentShotTransform(startPosition, startRotation);

        yield return FadeIn();

        ActivateTeleporter();

        if (cameraMoveDuration > 0f && Mathf.Abs(cameraMoveDistance) > 0.001f)
        {
            Vector3 moveDirection =
                startRotation * Vector3.forward;

            Transform target =
                FindTransformInScene("BuildingToWaterfrontTeleporter");
            if (target != null)
            {
                Vector3 targetDirection =
                    target.position - startPosition;
                targetDirection.y = 0f;

                if (targetDirection.sqrMagnitude > 0.001f)
                    moveDirection = targetDirection.normalized;
            }

            moveDirection.y = 0f;
            if (moveDirection.sqrMagnitude < 0.001f)
                moveDirection = startRotation * Vector3.forward;

            float targetDistance =
                target != null
                    ? Vector3.Distance(
                        new Vector3(startPosition.x, 0f, startPosition.z),
                        new Vector3(target.position.x, 0f, target.position.z))
                    : cameraMoveDistance;
            float moveDistance =
                Mathf.Clamp(
                    Mathf.Abs(cameraMoveDistance),
                    0f,
                    Mathf.Max(0f, targetDistance - 1f));

            Vector3 endPosition =
                startPosition + moveDirection.normalized * moveDistance;

            float elapsed = 0f;
            while (elapsed < cameraMoveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / cameraMoveDuration);

                if (smoothCameraMovement)
                    t = Mathf.SmoothStep(0f, 1f, t);

                SetCurrentShotTransform(
                    Vector3.Lerp(startPosition, endPosition, t),
                    startRotation);
                yield return null;
            }

            SetCurrentShotTransform(endPosition, startRotation);
        }

        if (viewHoldDuration > 0f)
            yield return new WaitForSeconds(viewHoldDuration);

        yield return FadeOut();
    }

    private void ActivateTeleporter()
    {
        foreach (GameObject vfxObject in teleporterVfxObjects)
        {
            if (vfxObject != null)
                vfxObject.SetActive(true);
        }

        if (teleporterActivateClip == null)
            return;

        if (effectSource == null)
        {
            effectSource = gameObject.AddComponent<AudioSource>();
            effectSource.playOnAwake = false;
            effectSource.loop = false;
            effectSource.spatialBlend = 0f;
        }

        effectSource.Stop();
        effectSource.volume = teleporterActivateVolume;
        effectSource.PlayOneShot(teleporterActivateClip);
    }

    private void ApplyShotTransform()
    {
        if (xrOrigin == null || xrHead == null)
            return;

        if (followPitch)
        {
            xrOrigin.rotation = currentShotRotation;
        }
        else if (followYaw)
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                currentShotRotation * Vector3.forward,
                Vector3.up);

            if (forward.sqrMagnitude > 0.001f)
            {
                xrOrigin.rotation = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);
            }
        }

        xrOrigin.position += currentShotPosition - xrHead.position;
    }

    private void SetCurrentShotTransform(
        Vector3 position,
        Quaternion rotation)
    {
        currentShotPosition = position;
        currentShotRotation = rotation;
        hasCurrentShot = true;
        ApplyShotTransform();
    }

    private void SaveOriginalXRTransform()
    {
        if (xrOrigin == null || originalXRTransformSaved)
            return;

        originalXROriginPosition = xrOrigin.position;
        originalXROriginRotation = xrOrigin.rotation;
        originalXRTransformSaved = true;
    }

    private void RestoreOriginalXRTransform()
    {
        if (xrOrigin == null || !originalXRTransformSaved)
            return;

        xrOrigin.position = originalXROriginPosition;
        xrOrigin.rotation = originalXROriginRotation;
        originalXRTransformSaved = false;
    }

    private IEnumerator FadeOut()
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeDuration));
    }

    private IEnumerator FadeIn()
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeIn(fadeDuration));
    }

    private void SetRemoteAvatarsVisible(bool visible)
    {
        if (!hideRemoteAvatars)
            return;

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player != null && !player.IsLocalNetworkRig)
                player.SetAvatarVisible(visible);
        }
    }

    private void SetTutorialCanvasVisible(bool visible)
    {
        if (hideTutorialCanvas && hardwareRig != null)
            hardwareRig.SetTutorialCanvasVisible(visible);
    }

    private void ResolveTeleporterVfxObjects()
    {
        if (teleporterVfxObjects != null &&
            teleporterVfxObjects.Length > 0)
            return;

        Transform teleporter =
            FindTransformInScene("BuildingToWaterfrontTeleporter");
        if (teleporter == null)
            return;

        GameObject fireCircle =
            FindChildGameObject(teleporter, "Fire Circle");
        GameObject splash =
            FindChildGameObject(teleporter, "Splash");

        if (fireCircle != null)
            fireCircle.SetActive(false);
        if (splash != null)
            splash.SetActive(false);

        teleporterVfxObjects =
            new[] { fireCircle, splash };
    }

    private static Transform FindTransformInScene(string targetName)
    {
        Transform[] transforms =
            FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (Transform transform in transforms)
        {
            if (transform.name == targetName)
                return transform;
        }

        return null;
    }

    private static GameObject FindChildGameObject(
        Transform parent,
        string targetName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(
                     true))
        {
            if (child.name == targetName)
                return child.gameObject;
        }

        return null;
    }

    private bool[] CaptureVfxInitialStates()
    {
        if (teleporterVfxObjects == null)
            return Array.Empty<bool>();

        bool[] states = new bool[teleporterVfxObjects.Length];
        for (int i = 0; i < teleporterVfxObjects.Length; i++)
        {
            if (teleporterVfxObjects[i] != null)
                states[i] = teleporterVfxObjects[i].activeSelf;
        }

        return states;
    }

    private void RestorePreviewVfxStates()
    {
        if (previewVfxInitialStates == null || teleporterVfxObjects == null)
            return;

        int count = Mathf.Min(
            previewVfxInitialStates.Length,
            teleporterVfxObjects.Length);

        for (int i = 0; i < count; i++)
        {
            if (teleporterVfxObjects[i] != null)
                teleporterVfxObjects[i].SetActive(previewVfxInitialStates[i]);
        }

        previewVfxInitialStates = null;
    }

    private void CleanupAfterCutscene()
    {
        if (!isPlaying)
        {
            if (effectSource != null)
                effectSource.Stop();

            if (previewPlayback)
                RestorePreviewVfxStates();

            previewPlayback = false;
            return;
        }

        isPlaying = false;
        hasCurrentShot = false;

        if (effectSource != null)
            effectSource.Stop();

        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        if (previewPlayback)
            RestorePreviewVfxStates();

        previewPlayback = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CutsceneFinished = null;
    }
}
