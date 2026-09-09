using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 하나의 컷 = 시작 시점 + 종료 지점 + 이동 시간 + 유지 시간.
/// End Point가 없으면 기존처럼 고정된 시점으로 재생한다.
/// </summary>
[System.Serializable]
public class Stage2CutsceneShot
{
    [Tooltip("이 컷의 시작 시점이 될 Cinemachine 카메라.")]
    public CinemachineCamera shotCamera;

    [Tooltip("카메라가 천천히 이동해서 도착할 지점. 비워두면 고정 카메라로 재생한다.")]
    public Transform endPoint;

    [Tooltip("시작 위치에서 End Point까지 이동하는 시간(초).")]
    public float moveDuration = 4f;

    [Tooltip("이동이 끝난 뒤 해당 시점을 유지하는 시간(초).")]
    public float holdDuration = 1f;
}

public class Stage2SkyCutscene : MonoBehaviour
{
    [Header("컷 (재생 순서: 하늘 → 식생 → 물고기 → 텔레포터)")]
    [SerializeField]
    private Stage2CutsceneShot skyShot = new Stage2CutsceneShot
    {
        moveDuration = 4.5f,
        holdDuration = 1f
    };

    [SerializeField]
    private Stage2CutsceneShot treeShot = new Stage2CutsceneShot
    {
        moveDuration = 6f,
        holdDuration = 1f
    };

    [SerializeField]
    private Stage2CutsceneShot fishShot = new Stage2CutsceneShot
    {
        moveDuration = 3.5f,
        holdDuration = 1f
    };

    [SerializeField]
    private Stage2CutsceneShot teleporterShot = new Stage2CutsceneShot
    {
        moveDuration = 4f,
        holdDuration = 1f
    };

    [Header("텔레포터 VFX")]
    [Tooltip("텔레포터 컷의 Fade In이 끝난 뒤 활성화할 VFX 오브젝트들.")]
    [SerializeField]
    private List<GameObject> teleporterVfxObjects = new List<GameObject>();

    [Header("카메라 이동")]
    [Tooltip("카메라 이동에 Ease In/Out을 적용한다.")]
    [SerializeField]
    private bool smoothCameraMovement = true;

    [Header("페이드")]
    [Tooltip("컷 전환 한 방향의 페이드 시간(초).")]
    [SerializeField]
    private float fadeDuration = 0.6f;

    [Tooltip("컷 사이 완전한 검정을 유지하는 시간(초).")]
    [SerializeField]
    private float blackHold = 0.2f;

    [Header("시점")]
    [Tooltip("컷 카메라의 좌우 방향(Yaw)에 맞춰 플레이어를 돌린다.")]
    [SerializeField]
    private bool followYaw = true;

    [Tooltip("상하 각도(Pitch)까지 강제한다. VR 멀미 가능성이 있으므로 권장하지 않는다.")]
    [SerializeField]
    private bool followPitch = false;

    [Header("연출 중 화면 정리")]
    [Tooltip("Resonance 안개/Bloom/Vignette를 끈다.")]
    [SerializeField]
    private bool disableResonance = true;

    [Tooltip("씬 자체의 RenderSettings 안개도 끈다.")]
    [SerializeField]
    private bool disableBuiltinFog = true;

    [Tooltip("컷씬 동안 상대 플레이어의 아바타를 숨긴다.")]
    [SerializeField]
    private bool hideRemoteAvatars = true;

    [Tooltip("컷씬 동안 이동/텔레포트를 잠근다.")]
    [SerializeField]
    private bool lockLocomotion = true;

    [Tooltip("컷씬 동안 왼손 컨트롤러의 TutorialCanvas를 숨긴다.")]
    [SerializeField]
    private bool hideTutorialCanvas = true;

    [Header("사운드")]
    [Tooltip("컷씬이 재생되는 동안 반복 재생할 새소리 클립.")]
    [SerializeField]
    private AudioClip birdSound;

    [Tooltip("새소리를 재생할 AudioSource. 비워두면 이 오브젝트에 하나 만들어 쓴다.")]
    [SerializeField]
    private AudioSource birdSoundSource;

    [Tooltip("새소리 볼륨.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float birdSoundVolume = 0.6f;

    [Header("Stage2 클리어 컷씬 사운드")]
    [Tooltip("Stage1 Timeline의 WorldRestore에 사용된 동일한 AudioClip.")]
    [SerializeField]
    private AudioClip worldRestoreClip;

    [Range(0f, 1f)]
    [SerializeField]
    private float worldRestoreVolume = 0.55f;

    [Tooltip("컷씬 전체에 반복 재생할 바람 Ambient 클립.")]
    [SerializeField]
    private AudioClip windClip;

    [Tooltip("런타임에 생성하는 Wind AudioSource의 기본 볼륨. Inspector Source를 연결하면 Source 볼륨을 유지한다.")]
    [Range(0f, 1f)]
    [SerializeField]
    private float windVolume = 0.15f;

    [Tooltip("Fish Shot 동안 반복 재생할 물고기 헤엄/물 움직임 클립.")]
    [SerializeField]
    private AudioClip fishSwimClip;

    [Range(0f, 1f)]
    [SerializeField]
    private float fishSwimVolume = 0.35f;

    [Tooltip("Teleporter VFX 활성화 순간 1회 재생할 클립.")]
    [SerializeField]
    private AudioClip teleporterActivateClip;

    [Range(0f, 1f)]
    [SerializeField]
    private float teleporterActivateVolume = 0.6f;

    [Tooltip("Stage2 Ambient 루프용 AudioSource. 비워두면 이 오브젝트에 하나 만들어 쓴다.")]
    [SerializeField]
    private AudioSource cutsceneAmbientSource;

    [Tooltip("Stage2 효과음 원샷/루프용 AudioSource. 비워두면 이 오브젝트에 하나 만들어 쓴다.")]
    [SerializeField]
    private AudioSource cutsceneEffectSource;

    [Tooltip("Sky 복원 시작 후 WorldRestore 효과음이 재생되기까지의 지연 시간")]
    [SerializeField] private float worldRestoreDelay = 3f;

    [Header("Debug")]
    [SerializeField]
    private bool xrReady;

    [SerializeField]
    private bool isPlaying;

    /// <summary>
    /// 컷씬이 완전히 끝났을 때 1회 발생.
    /// 이후 텔레포터 실제 사용 활성화에도 활용할 수 있다.
    /// </summary>
    public static event System.Action CutsceneFinished;

    // =================================================
    // Runtime
    // =================================================

    private Camera runtimeCamera;
    private Transform xrHead;
    private Transform xrOrigin;

    private ScreenFade screenFade;
    private HardwareRig hardwareRig;

    private readonly ResonanceCutsceneOverride resonance =
        new ResonanceCutsceneOverride();
    // 현재 컷의 가상 카메라 Transform
    private Vector3 currentShotPosition;
    private Quaternion currentShotRotation;
    private bool hasCurrentShot;

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;

    public bool IsPlaying => isPlaying;

    /// <summary>
    /// XR 리그가 준비되어 컷씬을 재생할 수 있는 상태인지.
    /// </summary>
    public bool CanPlay => xrReady && !isPlaying;

    // =================================================
    // XR 연결
    // =================================================

    private IEnumerator Start()
    {
        // XR Origin은 방 입장 후 생성되므로 기다린다.
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
            Debug.LogError(
                "[Stage2SkyCutscene] XR Origin을 찾지 못했습니다.",
                this);

            yield break;
        }

        screenFade =
            runtimeCamera.GetComponentInChildren<ScreenFade>(true);

        if (screenFade == null)
        {
            Debug.LogWarning(
                "[Stage2SkyCutscene] ScreenFade를 찾지 못했습니다. 페이드 없이 진행합니다.",
                this);
        }

        hardwareRig = FindFirstObjectByType<HardwareRig>();

        xrReady = true;

        Debug.Log(
            "[Stage2SkyCutscene] XR 연결 완료 - Origin: " + xrOrigin.name,
            this);
    }

    // =================================================
    // 시점 추종
    // =================================================

    private void LateUpdate()
    {
        if (!isPlaying || !hasCurrentShot)
            return;

        ApplyShotTransform();
    }

    /// <summary>
    /// 현재 계산된 컷 위치에 실제 XR Head가 오도록
    /// XR Origin을 보정한다.
    /// </summary>
    private void ApplyShotTransform()
    {
        if (!hasCurrentShot ||
            xrOrigin == null ||
            xrHead == null)
        {
            return;
        }

        if (followPitch)
        {
            xrOrigin.rotation = currentShotRotation;
        }
        else if (followYaw)
        {
            Vector3 targetForward =
                Vector3.ProjectOnPlane(
                    currentShotRotation * Vector3.forward,
                    Vector3.up);

            if (targetForward.sqrMagnitude > 0.001f)
            {
                xrOrigin.rotation =
                    Quaternion.LookRotation(
                        targetForward.normalized,
                        Vector3.up);
            }
        }

        // 실제 HMD 위치가 현재 컷 위치와 일치하도록
        // XR Origin을 보정한다.
        xrOrigin.position +=
            currentShotPosition - xrHead.position;
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

    // =================================================
    // 재생
    // =================================================

    /// <summary>
    /// PlantClearSequence가 호출한다.
    /// 재생을 시작했으면 true.
    /// </summary>
    public bool TryPlay(PlantClearSequence sequence)
    {
        if (sequence == null || !CanPlay)
            return false;

        StartCoroutine(PlayRoutine(sequence));
        return true;
    }

    private IEnumerator PlayRoutine(PlantClearSequence sequence)
    {
        isPlaying = true;

        StartBirdSound();
        StartCutsceneAmbient();

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);

        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        SaveOriginalXRTransform();

        // 1. 먼저 화면을 검게 만든다.
        yield return FadeOutRoutine();

        // 2. 검은 상태에서 안개/포스트 FX 제거
        if (disableResonance || disableBuiltinFog)
            resonance.Disable(disableBuiltinFog);

        sequence.ApplyFogColor();

        // 3. 하늘 복원
        yield return PlayShot(
            skyShot,
            () => StartSkyRestore(sequence),
            true);

        // 4. 식생 복원
        yield return PlayShot(
            treeShot,
            sequence.ReviveVegetation,
            false);

        // 5. 물고기 활성화
        yield return PlayShot(
            fishShot,
            sequence.ActivateObjects,
            true,
            StartFishSwimSound);

        StopFishSwimSound();

        // 6. 텔레포터 VFX 컷
        yield return PlayShot(
            teleporterShot,
            ActivateTeleporterVfx,
            false);

        // 7. 모든 컷 종료 후 원래 플레이어 위치 복귀
        hasCurrentShot = false;

        RestoreOriginalXRTransform();

        resonance.Restore();

        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        // 위치/효과 반영
        yield return null;

        // 원래 플레이어 시점으로 Fade In
        yield return FadeInRoutine();

        isPlaying = false;

        StopBirdSound();
        StopCutsceneAudio();

        Debug.Log(
            "[Stage2SkyCutscene] 컷씬 종료",
            this);

        CutsceneFinished?.Invoke();
    }

    /// <summary>
    /// 한 컷:
    ///
    /// 검은 화면
    /// → 시작 위치 배치
    /// → Fade In
    /// → 환경 효과 시작
    /// → 시작점에서 End Point까지 천천히 이동
    /// → 이동 중 Fade Out
    ///
    /// End Point가 없으면 기존처럼 고정 카메라로 재생한다.
    /// </summary>
    private IEnumerator PlayShot(
        Stage2CutsceneShot shot,
        System.Action onShotEffect,
        bool effectDuringBlack,
        System.Action onShotVisible = null)
    {
        if (shot == null || shot.shotCamera == null)
        {
            Debug.LogWarning(
                "[Stage2SkyCutscene] 컷 카메라가 비어 있어 효과만 적용하고 넘어갑니다.",
                this);

            onShotEffect?.Invoke();
            yield break;
        }

        Transform startPoint = shot.shotCamera.transform;

        Vector3 startPosition = startPoint.position;
        Quaternion startRotation = startPoint.rotation;

        // 검은 화면에서 시작 위치로 이동
        SetCurrentShotTransform(
            startPosition,
            startRotation);

        // 물고기처럼 화면이 보이기 전에 활성화해야 하는 효과
        if (effectDuringBlack)
            onShotEffect?.Invoke();

        if (blackHold > 0f)
            yield return new WaitForSeconds(blackHold);

        // 화면 표시
        yield return FadeInRoutine();

        onShotVisible?.Invoke();

        // 하늘/식생처럼 화면이 보인 후 변화 시작
        if (!effectDuringBlack)
            onShotEffect?.Invoke();

        // End Point가 있으면 실제 카메라 이동
        if (shot.endPoint != null &&
            shot.moveDuration > 0f)
        {
            Vector3 endPosition =
                shot.endPoint.position;

            Quaternion endRotation =
                shot.endPoint.rotation;

            float elapsed = 0f;
            float visibleDuration =
                Mathf.Max(0f, shot.moveDuration) +
                Mathf.Max(0f, shot.holdDuration);
            float totalMoveDuration =
                visibleDuration + Mathf.Max(0f, fadeDuration);
            Coroutine fadeOut = null;

            while (elapsed < totalMoveDuration)
            {
                elapsed += Time.deltaTime;

                float t =
                    Mathf.Clamp01(
                        elapsed / totalMoveDuration);

                if (fadeOut == null && elapsed >= visibleDuration)
                    fadeOut = StartCoroutine(FadeOutRoutine());

                // 부드러운 Ease In / Ease Out
                if (smoothCameraMovement)
                {
                    t = Mathf.SmoothStep(
                        0f,
                        1f,
                        t);
                }

                Vector3 position =
                    Vector3.Lerp(
                        startPosition,
                        endPosition,
                        t);

                Quaternion rotation =
                    Quaternion.Slerp(
                        startRotation,
                        endRotation,
                        t);

                SetCurrentShotTransform(
                    position,
                    rotation);

                yield return null;
            }

            if (fadeOut != null)
                yield return fadeOut;

            // 오차 없이 정확한 종료 위치 보장
            SetCurrentShotTransform(
                endPosition,
                endRotation);

            yield break;
        }
        else
        {
            // End Point가 없으면 기존 holdDuration 동안
            // 고정 카메라로 유지
            if (shot.holdDuration > 0f)
                yield return new WaitForSeconds(
                    shot.holdDuration);

            yield return FadeOutRoutine();
            yield break;
        }

        // 이동 완료 후 잠시 보여주기
        if (shot.holdDuration > 0f)
        {
            yield return new WaitForSeconds(
                shot.holdDuration);
        }

        // 다음 컷으로 넘어가기 전에 검게
        yield return FadeOutRoutine();
    }


    private void ActivateTeleporterVfx()
    {
        if (teleporterVfxObjects == null)
            return;

        foreach (GameObject vfxObject in teleporterVfxObjects)
        {
            if (vfxObject != null)
                vfxObject.SetActive(true);
        }

        PlayCutsceneOneShot(
            teleporterActivateClip,
            teleporterActivateVolume);
    }

    private void StartSkyRestore(PlantClearSequence sequence)
    {
        sequence.StartSkyboxFade();
        StartCoroutine(PlayWorldRestoreDelayed());
    }
    // =================================================
    // Fade
    // =================================================

    private IEnumerator FadeOutRoutine()
    {
        if (screenFade == null)
            yield break;

        yield return StartCoroutine(
            screenFade.FadeOut(fadeDuration));
    }

    private IEnumerator FadeInRoutine()
    {
        if (screenFade == null)
            yield break;

        yield return StartCoroutine(
            screenFade.FadeIn(fadeDuration));
    }

    // =================================================
    // 원격 아바타
    // =================================================

    private void SetRemoteAvatarsVisible(bool visible)
    {
        if (!hideRemoteAvatars)
            return;

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player == null ||
                player.IsLocalNetworkRig)
            {
                continue;
            }

            player.SetAvatarVisible(visible);
        }
    }

    // =================================================
    // 컨트롤러 UI
    // =================================================

    private void SetTutorialCanvasVisible(bool visible)
    {
        if (!hideTutorialCanvas ||
            hardwareRig == null)
        {
            return;
        }

        hardwareRig.SetTutorialCanvasVisible(visible);
    }

    // =================================================
    // 사운드
    // =================================================

    private void StartCutsceneAmbient()
    {
        if (windClip == null)
            return;

        bool createdSource = false;

        if (cutsceneAmbientSource == null)
        {
            cutsceneAmbientSource = gameObject.AddComponent<AudioSource>();
            cutsceneAmbientSource.playOnAwake = false;
            cutsceneAmbientSource.spatialBlend = 0f;
            createdSource = true;
        }

        cutsceneAmbientSource.Stop();
        cutsceneAmbientSource.clip = windClip;
        cutsceneAmbientSource.loop = true;

        if (createdSource)
            cutsceneAmbientSource.volume = windVolume;

        cutsceneAmbientSource.Play();
    }

    private void StartFishSwimSound()
    {
        if (fishSwimClip == null)
            return;

        if (cutsceneEffectSource == null)
        {
            cutsceneEffectSource = gameObject.AddComponent<AudioSource>();
            cutsceneEffectSource.playOnAwake = false;
            cutsceneEffectSource.spatialBlend = 0f;
        }

        cutsceneEffectSource.Stop();
        cutsceneEffectSource.clip = fishSwimClip;
        cutsceneEffectSource.loop = true;
        cutsceneEffectSource.volume = fishSwimVolume;
        cutsceneEffectSource.Play();
    }

    private void StopFishSwimSound()
    {
        if (cutsceneEffectSource == null ||
            cutsceneEffectSource.clip != fishSwimClip)
        {
            return;
        }

        cutsceneEffectSource.Stop();
        cutsceneEffectSource.clip = null;
    }

    private void PlayCutsceneOneShot(
        AudioClip clip,
        float volume)
    {
        if (clip == null)
            return;

        if (cutsceneEffectSource == null)
        {
            cutsceneEffectSource = gameObject.AddComponent<AudioSource>();
            cutsceneEffectSource.playOnAwake = false;
            cutsceneEffectSource.spatialBlend = 0f;
        }

        cutsceneEffectSource.volume = volume;
        cutsceneEffectSource.PlayOneShot(clip);
    }

    private void StopCutsceneAudio()
    {
        StopFishSwimSound();

        if (cutsceneAmbientSource != null)
            cutsceneAmbientSource.Stop();

        if (cutsceneEffectSource != null)
            cutsceneEffectSource.Stop();
    }

    private void StartBirdSound()
    {
        if (birdSoundSource == null)
        {
            if (birdSound == null)
                return;

            birdSoundSource =
                gameObject.AddComponent<AudioSource>();

            birdSoundSource.playOnAwake = false;
            birdSoundSource.spatialBlend = 0f;
        }

        if (birdSound != null)
            birdSoundSource.clip = birdSound;

        if (birdSoundSource.clip == null)
        {
            Debug.LogWarning(
                "[Stage2SkyCutscene] 새소리 클립이 비어 있어 재생하지 않습니다.",
                this);

            return;
        }

        birdSoundSource.loop = true;
        birdSoundSource.volume = birdSoundVolume;
        birdSoundSource.Play();
    }

    private void StopBirdSound()
    {
        if (birdSoundSource == null ||
            !birdSoundSource.isPlaying)
        {
            return;
        }

        birdSoundSource.Stop();
    }

    // =================================================
    // 안전장치
    // =================================================

    private void OnDisable()
    {
        StopAllCoroutines();
        StopBirdSound();
        StopCutsceneAudio();

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
    }

    private void OnDestroy()
    {
        resonance.Restore();
    }

    private IEnumerator PlayWorldRestoreDelayed()
    {
        if (worldRestoreClip == null)
            yield break;

        if (worldRestoreDelay > 0f)
            yield return new WaitForSeconds(worldRestoreDelay);

        if (cutsceneEffectSource == null)
        {
            cutsceneEffectSource = gameObject.AddComponent<AudioSource>();
            cutsceneEffectSource.playOnAwake = false;
            cutsceneEffectSource.spatialBlend = 0f;
        }

        cutsceneEffectSource.volume = worldRestoreVolume;
        cutsceneEffectSource.PlayOneShot(worldRestoreClip);
    }
}