using System;
using System.Collections;
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
    [SerializeField, Range(0f, 1f)] private float dirtyWaterVolume = 0.35f;
    [SerializeField] private AudioClip cleanWaterClip;
    [SerializeField, Range(0f, 1f)] private float cleanWaterStartVolume = 0.15f;
    [SerializeField, Range(0f, 1f)] private float cleanWaterVolume = 0.45f;
    [SerializeField] private float cleanWaterFadeOutDuration = 1.0f;
    [SerializeField] private AudioClip purificationClip;
    [SerializeField, Range(0f, 1f)] private float purificationVolume = 0.7f;

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

    [ContextMenu("Test Mission 1 Clear Cutscene")]
    private void TestMission1ClearCutscene()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Mission1ClearCutscene] Play Mode에서만 테스트할 수 있습니다.", this);
            return;
        }

        if (isPlaying)
        {
            Debug.LogWarning("[Mission1ClearCutscene] 컷씬이 이미 재생 중입니다.", this);
            return;
        }

        if (waterPurifyRoot == null)
        {
            Debug.LogWarning("[Mission1ClearCutscene] 테스트용 Water Purify Root를 연결하세요.", this);
            return;
        }

        WaterPurify[] waterPurifiers =
            waterPurifyRoot.GetComponentsInChildren<WaterPurify>(true);

        if (resetWaterBeforeTest)
        {
            StopWaterAudio();
            ResetWaterForTest(waterPurifiers);
        }

        if (!TryPlay(null))
            Debug.LogWarning("[Mission1ClearCutscene] 컷씬을 재생할 수 없습니다. XR/Shot 참조를 확인하세요.", this);
    }

    [ContextMenu("Reset Mission 1 Water For Test")]
    private void ResetMission1WaterForTest()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Mission1ClearCutscene] Play Mode에서만 물을 초기화할 수 있습니다.", this);
            return;
        }

        if (waterPurifyRoot == null)
        {
            Debug.LogWarning("[Mission1ClearCutscene] 테스트용 Water Purify Root를 연결하세요.", this);
            return;
        }

        StopWaterAudio();
        ResetWaterForTest(
            waterPurifyRoot.GetComponentsInChildren<WaterPurify>(true));
    }

    private static void ResetWaterForTest(WaterPurify[] waterPurifiers)
    {
        foreach (WaterPurify waterPurifier in waterPurifiers)
            if (waterPurifier != null)
                waterPurifier.ResetForTest();
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
            Debug.LogError("[Mission1ClearCutscene] XR Origin을 찾지 못했습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        EnsureWaterAudioSources();
        xrReady = true;
    }

    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }

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

    private IEnumerator PlayRoutine()
    {
        isPlaying = true;
        PlayDirtyWaterLoop();
        SaveOriginalXRTransform();

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);

        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        yield return FadeOutRoutine();
        resonance.Disable(true);
        yield return PlayShot1();

        yield return PlayShot(
            shot2Start,
            shot2End,
            shot2MoveDuration,
            shot2HoldDuration,
            StartWaterRiseAndRaiseVolume);

        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        yield return null;
        yield return FadeInRoutine();

        yield return FadeOutCleanWaterLoop();
        StopWaterAudio();
        isPlaying = false;
        InvokeCompletion();
    }

    private IEnumerator PlayShot1()
    {
        SetCurrentShotTransform(shot1Start.position, shot1Start.rotation);

        if (blackHold > 0f)
            yield return new WaitForSeconds(blackHold);

        yield return FadeInRoutine();

        float duration = Mathf.Max(shot1MoveDuration, 0f);
        float purificationStartTime =
            duration * Mathf.Clamp01(purificationStartNormalized);
        float purificationFadeTime = Mathf.Max(purificationFadeDuration, 0f);
        float elapsed = 0f;
        bool fadeStarted = false;
        bool cleanMaterialApplied = false;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = duration > 0f
                ? Mathf.Clamp01(elapsed / duration)
                : 1f;

            if (!fadeStarted && elapsed >= purificationStartTime)
            {
                fadeStarted = true;
                StartCoroutine(FadeOutRoutine(purificationFadeTime));
            }

            if (fadeStarted &&
                !cleanMaterialApplied &&
                elapsed >= purificationStartTime + purificationFadeTime)
            {
                ApplyCleanMaterial();
                PlayPurificationSound();
                PlayCleanWaterLoop();
                cleanMaterialApplied = true;
                StartCoroutine(FadeInRoutine(purificationFadeTime));
            }

            float easedProgress = smoothCameraMovement
                ? Mathf.SmoothStep(0f, 1f, progress)
                : progress;

            SetCurrentShotTransform(
                Vector3.Lerp(shot1Start.position, shot1End.position, easedProgress),
                Quaternion.Slerp(shot1Start.rotation, shot1End.rotation, easedProgress));
            yield return null;
        }

        if (!cleanMaterialApplied)
        {
            ApplyCleanMaterial();
            PlayPurificationSound();
            PlayCleanWaterLoop();
            yield return FadeInRoutine(purificationFadeTime);
        }

        SetCurrentShotTransform(shot1End.position, shot1End.rotation);

        if (shot1EndHoldDuration > 0f)
            yield return new WaitForSeconds(shot1EndHoldDuration);

        yield return FadeOutRoutine();
    }

    private IEnumerator PlayShot(
        Transform startPoint,
        Transform endPoint,
        float moveDuration,
        float holdDuration,
        Action onStart)
    {
        SetCurrentShotTransform(startPoint.position, startPoint.rotation);
        onStart?.Invoke();

        if (blackHold > 0f)
            yield return new WaitForSeconds(blackHold);

        yield return FadeInRoutine();

        yield return MoveShot(startPoint, endPoint, moveDuration);

        SetCurrentShotTransform(endPoint.position, endPoint.rotation);

        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        yield return FadeOutRoutine();
    }

    private IEnumerator MoveShot(
        Transform startPoint,
        Transform endPoint,
        float duration)
    {
        if (duration <= 0f)
        {
            SetCurrentShotTransform(endPoint.position, endPoint.rotation);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            if (smoothCameraMovement)
                t = Mathf.SmoothStep(0f, 1f, t);

            SetCurrentShotTransform(
                Vector3.Lerp(startPoint.position, endPoint.position, t),
                Quaternion.Slerp(startPoint.rotation, endPoint.rotation, t));
            yield return null;
        }

        SetCurrentShotTransform(endPoint.position, endPoint.rotation);
    }

    public void StartWaterRise(float duration)
    {
        foreach (WaterPurify waterPurifier in GetWaterPurifiers())
            if (waterPurifier != null)
                waterPurifier.StartWaterRise(duration);
    }

    private void StartWaterRiseAndRaiseVolume()
    {
        StartWaterRise(waterRiseDuration);
        StartCleanWaterVolumeRise(waterRiseDuration);
    }

    private void ApplyCleanMaterial()
    {
        foreach (WaterPurify waterPurifier in GetWaterPurifiers())
            if (waterPurifier != null)
                waterPurifier.ApplyCleanMaterial();
    }

    private void EnsureWaterAudioSources()
    {
        if (dirtyWaterSource == null)
        {
            dirtyWaterSource = gameObject.AddComponent<AudioSource>();
            ConfigureWaterAudioSource(dirtyWaterSource);
        }

        if (cleanWaterSource == null)
        {
            cleanWaterSource = gameObject.AddComponent<AudioSource>();
            ConfigureWaterAudioSource(cleanWaterSource);
        }

        if (purificationSource == null)
        {
            purificationSource = gameObject.AddComponent<AudioSource>();
            purificationSource.playOnAwake = false;
            purificationSource.loop = false;
            purificationSource.spatialBlend = 0f;
        }
    }

    private static void ConfigureWaterAudioSource(AudioSource source)
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

        dirtyWaterSource.clip = dirtyWaterClip;
        dirtyWaterSource.volume = dirtyWaterVolume;
        if (!dirtyWaterSource.isPlaying)
            dirtyWaterSource.Play();
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

        cleanWaterSource.clip = cleanWaterClip;
        cleanWaterSource.volume = cleanWaterStartVolume;
        if (!cleanWaterSource.isPlaying)
            cleanWaterSource.Play();
    }

    private void PlayPurificationSound()
    {
        EnsureWaterAudioSources();

        if (purificationClip == null)
            return;

        purificationSource.volume = purificationVolume;
        purificationSource.PlayOneShot(purificationClip);
    }

    private void StartCleanWaterVolumeRise(float duration)
    {
        if (cleanWaterVolumeCoroutine != null)
            StopCoroutine(cleanWaterVolumeCoroutine);

        cleanWaterVolumeCoroutine = StartCoroutine(
            RaiseCleanWaterVolume(Mathf.Max(duration, 0f)));
    }

    private IEnumerator RaiseCleanWaterVolume(float duration)
    {
        EnsureWaterAudioSources();

        if (cleanWaterSource == null || cleanWaterClip == null)
        {
            cleanWaterVolumeCoroutine = null;
            yield break;
        }

        if (!cleanWaterSource.isPlaying)
            cleanWaterSource.Play();

        if (duration <= 0f)
        {
            cleanWaterSource.volume = cleanWaterVolume;
            cleanWaterVolumeCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            cleanWaterSource.volume = Mathf.Lerp(
                cleanWaterStartVolume,
                cleanWaterVolume,
                Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }

        cleanWaterSource.volume = cleanWaterVolume;
        cleanWaterVolumeCoroutine = null;
    }

    private void StopWaterAudio()
    {
        if (dirtyWaterSource != null)
            dirtyWaterSource.Stop();

        if (cleanWaterSource != null)
            cleanWaterSource.Stop();

        if (purificationSource != null)
            purificationSource.Stop();

        if (cleanWaterVolumeCoroutine != null)
        {
            StopCoroutine(cleanWaterVolumeCoroutine);
            cleanWaterVolumeCoroutine = null;
        }
    }

    private IEnumerator FadeOutCleanWaterLoop()
    {
        if (cleanWaterSource == null)
            yield break;

        float duration = Mathf.Max(cleanWaterFadeOutDuration, 0f);
        float startVolume = cleanWaterSource.volume;

        if (duration <= 0f || !cleanWaterSource.isPlaying)
        {
            cleanWaterSource.Stop();
            cleanWaterSource.volume = cleanWaterStartVolume;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            cleanWaterSource.volume = Mathf.Lerp(startVolume, 0f, t);
            yield return null;
        }

        cleanWaterSource.volume = 0f;
        cleanWaterSource.Stop();
        cleanWaterSource.volume = cleanWaterStartVolume;
    }

    private WaterPurify[] GetWaterPurifiers()
    {
        return waterPurifyRoot != null
            ? waterPurifyRoot.GetComponentsInChildren<WaterPurify>(true)
            : Array.Empty<WaterPurify>();
    }

    private bool HasRequiredShotPoints()
    {
        return shot1Start != null &&
               shot1End != null &&
               shot2Start != null &&
               shot2End != null;
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
                xrOrigin.rotation = Quaternion.LookRotation(
                    forward.normalized,
                    Vector3.up);
        }

        xrOrigin.position += currentShotPosition - xrHead.position;
    }

    private void SetCurrentShotTransform(Vector3 position, Quaternion rotation)
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

    private IEnumerator FadeOutRoutine(float duration = -1f)
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(
                duration >= 0f ? duration : fadeDuration));
    }

    private IEnumerator FadeInRoutine(float duration = -1f)
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeIn(
                duration >= 0f ? duration : fadeDuration));
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

    private void InvokeCompletion()
    {
        if (completionInvoked)
            return;

        completionInvoked = true;
        Action callback = completion;
        completion = null;
        callback?.Invoke();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        StopWaterAudio();

        if (!isPlaying)
            return;

        isPlaying = false;
        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        InvokeCompletion();
    }
}
