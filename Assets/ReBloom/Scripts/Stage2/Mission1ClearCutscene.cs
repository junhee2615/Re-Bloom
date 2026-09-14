using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Stage 2 Mission 1 수로 복구 클리어 컷씬.
public class Mission1ClearCutscene : MonoBehaviour
{
    [Header("Shot 위치")]
    [SerializeField] private Transform shot1Start;
    [SerializeField] private Transform shot1End;
    [SerializeField] private Transform shot2Start;
    [SerializeField] private Transform shot2End;

    [Header("재생 시간")]
    [SerializeField] private float shot1MoveDuration = 5.5f;

    [Range(0f, 1f)]
    [SerializeField] private float purificationStartNormalized = 0.45f;

    [SerializeField] private float purificationFadeDuration = 0.6f;
    [SerializeField] private float shot1EndHoldDuration = 0.5f;

    [SerializeField] private float shot2MoveDuration = 5f;
    [SerializeField] private float shot2HoldDuration = 1f;

    [SerializeField] private float waterRiseDuration = 5f;

    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private float blackHold = 0.2f;

    [Header("카메라")]
    [SerializeField] private bool smoothCameraMovement = true;
    [SerializeField] private bool followYaw = true;
    [SerializeField] private bool followPitch;

    [Header("Water Audio")]
    [SerializeField] private AudioClip dirtyWaterClip;

    [SerializeField, Range(0f, 1f)]
    private float dirtyWaterVolume = 0.35f;

    [SerializeField] private AudioClip cleanWaterClip;

    [SerializeField, Range(0f, 1f)]
    private float cleanWaterStartVolume = 0.15f;

    [SerializeField, Range(0f, 1f)]
    private float cleanWaterVolume = 0.45f;

    [SerializeField]
    private float cleanWaterFadeOutDuration = 1.0f;

    [SerializeField] private AudioClip purificationClip;

    [SerializeField, Range(0f, 1f)]
    private float purificationVolume = 0.7f;

    [Header("테스트")]
    [SerializeField] private Transform waterPurifyRoot;
    [SerializeField] private bool resetWaterBeforeTest = true;

    [Header("연출 중 화면 정리")]
    [SerializeField] private bool hideRemoteAvatars = true;
    [SerializeField] private bool lockLocomotion = true;
    [SerializeField] private bool hideTutorialCanvas = true;

    [Header("Debug")]
    [SerializeField] private bool xrReady;
    [SerializeField] private bool isPlaying;

    private Camera runtimeCamera;
    private Transform xrHead;
    private Transform xrOrigin;

    private ScreenFade screenFade;
    private HardwareRig hardwareRig;

    private AudioSource dirtyWaterSource;
    private AudioSource cleanWaterSource;
    private AudioSource purificationSource;

    private Coroutine cleanWaterVolumeCoroutine;

    // 컷씬 시작 시 XR Origin > PhysicsHands 아래의
    // 모든 Renderer를 런타임에 찾아 저장한다.
    private readonly List<Renderer> localHandVisualRenderers =
        new List<Renderer>();

    private readonly List<bool> localHandVisualOriginalStates =
        new List<bool>();

    private bool localHandVisualsHidden;

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;

    private Vector3 currentShotPosition;
    private Quaternion currentShotRotation;
    private bool hasCurrentShot;

    private Action completion;
    private bool completionInvoked;

    private readonly ResonanceCutsceneOverride resonance =
        new ResonanceCutsceneOverride();

    public bool IsPlaying => isPlaying;
    public bool CanPlay => xrReady && !isPlaying;


    // ============================================================
    // Test
    // ============================================================

    [ContextMenu("Test Mission 1 Clear Cutscene")]
    private void TestMission1ClearCutscene()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] Play Mode에서만 테스트할 수 있습니다.",
                this);
            return;
        }

        if (isPlaying)
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] 컷씬이 이미 재생 중입니다.",
                this);
            return;
        }

        if (waterPurifyRoot == null)
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] 테스트용 Water Purify Root를 연결하세요.",
                this);
            return;
        }

        WaterPurify[] waterPurifiers =
            waterPurifyRoot.GetComponentsInChildren<WaterPurify>(true);

        if (resetWaterBeforeTest)
        {
            StopWaterAudio();
            RestoreLocalHandVisuals();
            ResetWaterForTest(waterPurifiers);
        }

        if (!TryPlay(null))
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] 컷씬을 재생할 수 없습니다. XR/Shot 참조를 확인하세요.",
                this);
        }
    }


    [ContextMenu("Reset Mission 1 Water For Test")]
    private void ResetMission1WaterForTest()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] Play Mode에서만 물을 초기화할 수 있습니다.",
                this);
            return;
        }

        if (waterPurifyRoot == null)
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] 테스트용 Water Purify Root를 연결하세요.",
                this);
            return;
        }

        StopWaterAudio();
        RestoreLocalHandVisuals();

        ResetWaterForTest(
            waterPurifyRoot.GetComponentsInChildren<WaterPurify>(true));
    }


    private static void ResetWaterForTest(
        WaterPurify[] waterPurifiers)
    {
        foreach (WaterPurify waterPurifier in waterPurifiers)
        {
            if (waterPurifier != null)
                waterPurifier.ResetForTest();
        }
    }


    // ============================================================
    // Initialize
    // ============================================================

    private IEnumerator Start()
    {
        // XR System이 런타임에 생성될 수 있으므로
        // Main Camera가 준비될 때까지 대기한다.
        while (runtimeCamera == null)
        {
            runtimeCamera = Camera.main;
            yield return null;
        }

        xrHead = runtimeCamera.transform;

        // Main Camera의 부모를 거슬러 올라가며
        // 현재 런타임 XR Origin을 찾는다.
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
            Debug.LogError(
                "[Mission1ClearCutscene] XR Origin을 찾지 못했습니다.",
                this);

            yield break;
        }

        screenFade =
            runtimeCamera.GetComponentInChildren<ScreenFade>(true);

        hardwareRig =
            FindFirstObjectByType<HardwareRig>();

        EnsureWaterAudioSources();

        xrReady = true;
    }


    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }


    // ============================================================
    // Public Play
    // ============================================================

    public bool TryPlay(Action onFinished)
    {
        if (!CanPlay)
            return false;

        if (!HasRequiredShotPoints())
            return false;

        completion = onFinished;
        completionInvoked = false;

        StartCoroutine(PlayRoutine());

        return true;
    }


    // ============================================================
    // Main Cutscene
    // ============================================================

    private IEnumerator PlayRoutine()
    {
        isPlaying = true;

        PlayDirtyWaterLoop();

        SaveOriginalXRTransform();

        if (lockLocomotion && hardwareRig != null)
        {
            hardwareRig.SetLocomotionLocked(true);
        }

        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        // 실제 PhysicsHands Renderer 숨김
        HideLocalHandVisuals();

        yield return FadeOutRoutine();

        resonance.Disable(true);

        // Shot 1
        yield return PlayShot1();

        // Shot 2
        yield return PlayShot(
            shot2Start,
            shot2End,
            shot2MoveDuration,
            shot2HoldDuration,
            StartWaterRiseAndRaiseVolume);

        hasCurrentShot = false;

        RestoreOriginalXRTransform();

        resonance.Restore();

        // XR Origin이 원래 위치로 돌아온 뒤 손을 다시 표시
        RestoreLocalHandVisuals();

        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
        {
            hardwareRig.SetLocomotionLocked(false);
        }

        yield return null;

        yield return FadeInRoutine();

        // 깨끗한 물 소리 자연스럽게 Fade Out
        yield return FadeOutCleanWaterLoop();

        StopWaterAudio();

        isPlaying = false;

        InvokeCompletion();
    }


    // ============================================================
    // Shot 1
    // ============================================================

    private IEnumerator PlayShot1()
    {
        SetCurrentShotTransform(
            shot1Start.position,
            shot1Start.rotation);

        if (blackHold > 0f)
        {
            yield return new WaitForSeconds(blackHold);
        }

        yield return FadeInRoutine();

        float duration =
            Mathf.Max(shot1MoveDuration, 0f);

        float purificationStartTime =
            duration *
            Mathf.Clamp01(purificationStartNormalized);

        float purificationFadeTime =
            Mathf.Max(purificationFadeDuration, 0f);

        float elapsed = 0f;

        bool fadeStarted = false;
        bool cleanMaterialApplied = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float progress =
                duration > 0f
                    ? Mathf.Clamp01(elapsed / duration)
                    : 1f;

            // Shot 1 이동 중 정화 Fade 시작
            if (!fadeStarted &&
                elapsed >= purificationStartTime)
            {
                fadeStarted = true;

                StartCoroutine(
                    FadeOutRoutine(
                        purificationFadeTime));
            }

            // Fade Out이 끝난 시점에
            // Material 및 사운드 전환
            if (fadeStarted &&
                !cleanMaterialApplied &&
                elapsed >=
                purificationStartTime +
                purificationFadeTime)
            {
                ApplyCleanMaterial();

                PlayPurificationSound();

                PlayCleanWaterLoop();

                cleanMaterialApplied = true;

                StartCoroutine(
                    FadeInRoutine(
                        purificationFadeTime));
            }

            float easedProgress =
                smoothCameraMovement
                    ? Mathf.SmoothStep(
                        0f,
                        1f,
                        progress)
                    : progress;

            SetCurrentShotTransform(
                Vector3.Lerp(
                    shot1Start.position,
                    shot1End.position,
                    easedProgress),

                Quaternion.Slerp(
                    shot1Start.rotation,
                    shot1End.rotation,
                    easedProgress));

            yield return null;
        }

        // duration이 너무 짧은 예외 상황
        if (!cleanMaterialApplied)
        {
            ApplyCleanMaterial();

            PlayPurificationSound();

            PlayCleanWaterLoop();

            yield return
                FadeInRoutine(
                    purificationFadeTime);
        }

        SetCurrentShotTransform(
            shot1End.position,
            shot1End.rotation);

        if (shot1EndHoldDuration > 0f)
        {
            yield return
                new WaitForSeconds(
                    shot1EndHoldDuration);
        }

        yield return FadeOutRoutine();
    }


    // ============================================================
    // Generic Shot
    // ============================================================

    private IEnumerator PlayShot(
        Transform startPoint,
        Transform endPoint,
        float moveDuration,
        float holdDuration,
        Action onStart)
    {
        SetCurrentShotTransform(
            startPoint.position,
            startPoint.rotation);

        onStart?.Invoke();

        if (blackHold > 0f)
        {
            yield return
                new WaitForSeconds(
                    blackHold);
        }

        yield return FadeInRoutine();

        yield return MoveShot(
            startPoint,
            endPoint,
            moveDuration);

        SetCurrentShotTransform(
            endPoint.position,
            endPoint.rotation);

        if (holdDuration > 0f)
        {
            yield return
                new WaitForSeconds(
                    holdDuration);
        }

        yield return FadeOutRoutine();
    }


    private IEnumerator MoveShot(
        Transform startPoint,
        Transform endPoint,
        float duration)
    {
        if (duration <= 0f)
        {
            SetCurrentShotTransform(
                endPoint.position,
                endPoint.rotation);

            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration);

            if (smoothCameraMovement)
            {
                t = Mathf.SmoothStep(
                    0f,
                    1f,
                    t);
            }

            SetCurrentShotTransform(
                Vector3.Lerp(
                    startPoint.position,
                    endPoint.position,
                    t),

                Quaternion.Slerp(
                    startPoint.rotation,
                    endPoint.rotation,
                    t));

            yield return null;
        }

        SetCurrentShotTransform(
            endPoint.position,
            endPoint.rotation);
    }


    // ============================================================
    // Water
    // ============================================================

    public void StartWaterRise(float duration)
    {
        foreach (
            WaterPurify waterPurifier
            in GetWaterPurifiers())
        {
            if (waterPurifier != null)
            {
                waterPurifier.StartWaterRise(
                    duration);
            }
        }
    }


    private void StartWaterRiseAndRaiseVolume()
    {
        StartWaterRise(
            waterRiseDuration);

        StartCleanWaterVolumeRise(
            waterRiseDuration);
    }


    private void ApplyCleanMaterial()
    {
        foreach (
            WaterPurify waterPurifier
            in GetWaterPurifiers())
        {
            if (waterPurifier != null)
            {
                waterPurifier.ApplyCleanMaterial();
            }
        }
    }


    private WaterPurify[] GetWaterPurifiers()
    {
        return waterPurifyRoot != null
            ? waterPurifyRoot
                .GetComponentsInChildren<WaterPurify>(true)
            : Array.Empty<WaterPurify>();
    }


    // ============================================================
    // Water Audio
    // ============================================================

    private void EnsureWaterAudioSources()
    {
        if (dirtyWaterSource == null)
        {
            dirtyWaterSource =
                gameObject.AddComponent<AudioSource>();

            ConfigureWaterAudioSource(
                dirtyWaterSource);
        }

        if (cleanWaterSource == null)
        {
            cleanWaterSource =
                gameObject.AddComponent<AudioSource>();

            ConfigureWaterAudioSource(
                cleanWaterSource);
        }

        if (purificationSource == null)
        {
            purificationSource =
                gameObject.AddComponent<AudioSource>();

            purificationSource.playOnAwake =
                false;

            purificationSource.loop =
                false;

            purificationSource.spatialBlend =
                0f;
        }
    }


    private static void ConfigureWaterAudioSource(
        AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
    }


    private void PlayDirtyWaterLoop()
    {
        EnsureWaterAudioSources();

        cleanWaterSource.Stop();

        if (dirtyWaterClip == null)
        {
            dirtyWaterSource.Stop();
            return;
        }

        dirtyWaterSource.clip =
            dirtyWaterClip;

        dirtyWaterSource.volume =
            dirtyWaterVolume;

        if (!dirtyWaterSource.isPlaying)
        {
            dirtyWaterSource.Play();
        }
    }


    private void PlayCleanWaterLoop()
    {
        EnsureWaterAudioSources();

        dirtyWaterSource.Stop();

        if (cleanWaterClip == null)
        {
            cleanWaterSource.Stop();
            return;
        }

        cleanWaterSource.clip =
            cleanWaterClip;

        cleanWaterSource.volume =
            cleanWaterStartVolume;

        if (!cleanWaterSource.isPlaying)
        {
            cleanWaterSource.Play();
        }
    }


    private void PlayPurificationSound()
    {
        EnsureWaterAudioSources();

        if (purificationClip == null)
            return;

        purificationSource.volume =
            purificationVolume;

        purificationSource.PlayOneShot(
            purificationClip);
    }


    private void StartCleanWaterVolumeRise(
        float duration)
    {
        if (cleanWaterVolumeCoroutine != null)
        {
            StopCoroutine(
                cleanWaterVolumeCoroutine);
        }

        cleanWaterVolumeCoroutine =
            StartCoroutine(
                RaiseCleanWaterVolume(
                    Mathf.Max(
                        duration,
                        0f)));
    }


    private IEnumerator RaiseCleanWaterVolume(
        float duration)
    {
        EnsureWaterAudioSources();

        if (cleanWaterSource == null ||
            cleanWaterClip == null)
        {
            cleanWaterVolumeCoroutine =
                null;

            yield break;
        }

        if (!cleanWaterSource.isPlaying)
        {
            cleanWaterSource.Play();
        }

        if (duration <= 0f)
        {
            cleanWaterSource.volume =
                cleanWaterVolume;

            cleanWaterVolumeCoroutine =
                null;

            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration);

            cleanWaterSource.volume =
                Mathf.Lerp(
                    cleanWaterStartVolume,
                    cleanWaterVolume,
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        t));

            yield return null;
        }

        cleanWaterSource.volume =
            cleanWaterVolume;

        cleanWaterVolumeCoroutine =
            null;
    }


    private IEnumerator FadeOutCleanWaterLoop()
    {
        if (cleanWaterSource == null)
            yield break;

        float duration =
            Mathf.Max(
                cleanWaterFadeOutDuration,
                0f);

        float startVolume =
            cleanWaterSource.volume;

        if (duration <= 0f ||
            !cleanWaterSource.isPlaying)
        {
            cleanWaterSource.Stop();

            cleanWaterSource.volume =
                cleanWaterStartVolume;

            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed / duration);

            cleanWaterSource.volume =
                Mathf.Lerp(
                    startVolume,
                    0f,
                    t);

            yield return null;
        }

        cleanWaterSource.volume =
            0f;

        cleanWaterSource.Stop();

        cleanWaterSource.volume =
            cleanWaterStartVolume;
    }


    private void StopWaterAudio()
    {
        if (dirtyWaterSource != null)
        {
            dirtyWaterSource.Stop();
        }

        if (cleanWaterSource != null)
        {
            cleanWaterSource.Stop();
        }

        if (purificationSource != null)
        {
            purificationSource.Stop();
        }

        if (cleanWaterVolumeCoroutine != null)
        {
            StopCoroutine(
                cleanWaterVolumeCoroutine);

            cleanWaterVolumeCoroutine =
                null;
        }
    }


    // ============================================================
    // XR Shot
    // ============================================================

    private bool HasRequiredShotPoints()
    {
        return shot1Start != null &&
               shot1End != null &&
               shot2Start != null &&
               shot2End != null;
    }


    private void ApplyShotTransform()
    {
        if (xrOrigin == null ||
            xrHead == null)
        {
            return;
        }

        if (followPitch)
        {
            xrOrigin.rotation =
                currentShotRotation;
        }
        else if (followYaw)
        {
            Vector3 forward =
                Vector3.ProjectOnPlane(
                    currentShotRotation *
                    Vector3.forward,
                    Vector3.up);

            if (forward.sqrMagnitude >
                0.001f)
            {
                xrOrigin.rotation =
                    Quaternion.LookRotation(
                        forward.normalized,
                        Vector3.up);
            }
        }

        xrOrigin.position +=
            currentShotPosition -
            xrHead.position;
    }


    private void SetCurrentShotTransform(
        Vector3 position,
        Quaternion rotation)
    {
        currentShotPosition =
            position;

        currentShotRotation =
            rotation;

        hasCurrentShot =
            true;

        ApplyShotTransform();
    }


    private void SaveOriginalXRTransform()
    {
        if (xrOrigin == null ||
            originalXRTransformSaved)
        {
            return;
        }

        originalXROriginPosition =
            xrOrigin.position;

        originalXROriginRotation =
            xrOrigin.rotation;

        originalXRTransformSaved =
            true;
    }


    private void RestoreOriginalXRTransform()
    {
        if (xrOrigin == null ||
            !originalXRTransformSaved)
        {
            return;
        }

        xrOrigin.position =
            originalXROriginPosition;

        xrOrigin.rotation =
            originalXROriginRotation;

        originalXRTransformSaved =
            false;
    }


    // ============================================================
    // Fade
    // ============================================================

    private IEnumerator FadeOutRoutine(
        float duration = -1f)
    {
        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeOut(
                    duration >= 0f
                        ? duration
                        : fadeDuration));
        }
    }


    private IEnumerator FadeInRoutine(
        float duration = -1f)
    {
        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeIn(
                    duration >= 0f
                        ? duration
                        : fadeDuration));
        }
    }


    // ============================================================
    // Avatar / UI
    // ============================================================

    private void SetRemoteAvatarsVisible(
        bool visible)
    {
        if (!hideRemoteAvatars)
            return;

        foreach (
            NetworkPlayer player
            in NetworkPlayer.All)
        {
            if (player != null &&
                !player.IsLocalNetworkRig)
            {
                player.SetAvatarVisible(
                    visible);
            }
        }
    }


    private void SetTutorialCanvasVisible(
        bool visible)
    {
        if (hideTutorialCanvas &&
            hardwareRig != null)
        {
            hardwareRig.SetTutorialCanvasVisible(
                visible);
        }
    }


    // ============================================================
    // Local Physics Hand Visual
    // ============================================================

    private void HideLocalHandVisuals()
    {
        if (localHandVisualsHidden)
            return;

        ResolveLocalHandVisuals();

        localHandVisualOriginalStates.Clear();

        foreach (
            Renderer renderer
            in localHandVisualRenderers)
        {
            if (renderer == null)
                continue;

            localHandVisualOriginalStates.Add(
                renderer.enabled);

            renderer.enabled =
                false;
        }

        if (localHandVisualRenderers.Count > 0)
        {
            localHandVisualsHidden =
                true;

            Debug.Log(
                $"[Mission1ClearCutscene] PhysicsHands Renderer " +
                $"{localHandVisualRenderers.Count}개 숨김",
                this);
        }
        else
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] " +
                "PhysicsHands 아래 Renderer를 찾지 못했습니다.",
                this);
        }
    }


    private void RestoreLocalHandVisuals()
    {
        if (!localHandVisualsHidden)
            return;

        int stateIndex = 0;

        foreach (
            Renderer renderer
            in localHandVisualRenderers)
        {
            if (renderer == null)
                continue;

            if (stateIndex <
                localHandVisualOriginalStates.Count)
            {
                renderer.enabled =
                    localHandVisualOriginalStates[
                        stateIndex];
            }

            stateIndex++;
        }

        localHandVisualOriginalStates.Clear();

        localHandVisualRenderers.Clear();

        localHandVisualsHidden =
            false;
    }


    private void ResolveLocalHandVisuals()
    {
        localHandVisualRenderers.Clear();

        if (xrOrigin == null)
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] " +
                "XR Origin이 없어 PhysicsHands를 검색할 수 없습니다.",
                this);

            return;
        }

        Transform physicsHands =
            null;

        Transform[] allTransforms =
            xrOrigin.GetComponentsInChildren<Transform>(
                true);

        foreach (
            Transform child
            in allTransforms)
        {
            if (child.name ==
                "PhysicsHands")
            {
                physicsHands =
                    child;

                break;
            }
        }

        if (physicsHands == null)
        {
            Debug.LogWarning(
                "[Mission1ClearCutscene] " +
                "XR Origin 아래에서 PhysicsHands를 찾지 못했습니다.",
                this);

            return;
        }

        Renderer[] renderers =
            physicsHands.GetComponentsInChildren<Renderer>(
                true);

        foreach (
            Renderer renderer
            in renderers)
        {
            if (renderer == null)
                continue;

            localHandVisualRenderers.Add(
                renderer);

            Debug.Log(
                $"[Mission1ClearCutscene] " +
                $"손 Renderer 발견: " +
                $"{renderer.gameObject.name} / " +
                $"{renderer.GetType().Name}",
                renderer.gameObject);
        }
    }


    // ============================================================
    // Completion
    // ============================================================

    private void InvokeCompletion()
    {
        if (completionInvoked)
            return;

        completionInvoked =
            true;

        Action callback =
            completion;

        completion =
            null;

        callback?.Invoke();
    }


    // ============================================================
    // Safety Restore
    // ============================================================

    private void OnDisable()
    {
        StopAllCoroutines();

        StopWaterAudio();

        RestoreLocalHandVisuals();

        if (!isPlaying)
            return;

        isPlaying =
            false;

        hasCurrentShot =
            false;

        RestoreOriginalXRTransform();

        resonance.Restore();

        SetRemoteAvatarsVisible(
            true);

        SetTutorialCanvasVisible(
            true);

        if (lockLocomotion &&
            hardwareRig != null)
        {
            hardwareRig.SetLocomotionLocked(
                false);
        }

        InvokeCompletion();
    }
}