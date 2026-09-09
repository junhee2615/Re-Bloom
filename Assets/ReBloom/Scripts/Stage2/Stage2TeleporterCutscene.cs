using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// 뿌리 활성화 전체 완료 후 텔레포터를 연출한다.
public class Stage2TeleporterCutscene : MonoBehaviour
{
    [Header("텔레포터 컷")]
    [SerializeField]
    private Stage2CutsceneShot teleporterShot = new Stage2CutsceneShot
    {
        moveDuration = 4f,
        holdDuration = 1f
    };

    [Header("텔레포터 VFX")]
    [SerializeField] private List<GameObject> teleporterVfxObjects = new List<GameObject>();

    [Header("텔레포터 효과음")]
    [SerializeField] private AudioClip teleporterActivateClip;
    [Range(0f, 1f)]
    [SerializeField] private float teleporterActivateVolume = 0.6f;
    [SerializeField] private AudioSource effectSource;

    [Header("카메라 / 페이드")]
    [SerializeField] private bool smoothCameraMovement = true;
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private float blackHold = 0.2f;
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
    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;
    private Vector3 currentShotPosition;
    private Quaternion currentShotRotation;
    private bool hasCurrentShot;
    private readonly ResonanceCutsceneOverride resonance = new ResonanceCutsceneOverride();

    public bool IsPlaying => isPlaying;
    public bool CanPlay => xrReady && !isPlaying;

    private void OnEnable()
    {
        RootMissionManager.AllRootsActivated += OnAllRootsActivated;
    }

    private void OnDisable()
    {
        RootMissionManager.AllRootsActivated -= OnAllRootsActivated;
        StopAllCoroutines();
        CleanupAfterCutscene();
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
            Debug.LogError("[Stage2TeleporterCutscene] XR Origin을 찾지 못했습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        xrReady = true;

        if (pendingPlay)
        {
            pendingPlay = false;
            TryPlay();
        }
    }

    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }

    private void OnAllRootsActivated()
    {
        if (!CanPlay)
        {
            pendingPlay = true;
            return;
        }

        TryPlay();
    }

    public bool TryPlay()
    {
        if (!CanPlay)
            return false;

        StartCoroutine(PlayRoutine());
        return true;
    }

    private IEnumerator PlayRoutine()
    {
        isPlaying = true;
        SaveOriginalXRTransform();

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);
        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        yield return FadeOutRoutine();
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
        yield return FadeInRoutine();
        isPlaying = false;

        Debug.Log("[Stage2TeleporterCutscene] 텔레포터 컷씬 종료", this);
        CutsceneFinished?.Invoke();
    }

    private IEnumerator PlayTeleporterShot()
    {
        if (teleporterShot == null || teleporterShot.shotCamera == null)
        {
            Debug.LogWarning("[Stage2TeleporterCutscene] 텔레포터 카메라가 비어 있습니다.", this);
            ActivateTeleporterVfx();
            yield return FadeInRoutine();
            yield return FadeOutRoutine();
            yield break;
        }

        Transform startPoint = teleporterShot.shotCamera.transform;
        Vector3 startPosition = startPoint.position;
        Quaternion startRotation = startPoint.rotation;
        SetCurrentShotTransform(startPosition, startRotation);

        if (blackHold > 0f)
            yield return new WaitForSeconds(blackHold);

        yield return FadeInRoutine();
        ActivateTeleporterVfx();

        if (teleporterShot.endPoint != null && teleporterShot.moveDuration > 0f)
        {
            Vector3 endPosition = teleporterShot.endPoint.position;
            Quaternion endRotation = teleporterShot.endPoint.rotation;
            float elapsed = 0f;
            while (elapsed < teleporterShot.moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / teleporterShot.moveDuration);
                if (smoothCameraMovement)
                    t = Mathf.SmoothStep(0f, 1f, t);
                SetCurrentShotTransform(
                    Vector3.Lerp(startPosition, endPosition, t),
                    Quaternion.Slerp(startRotation, endRotation, t));
                yield return null;
            }
            SetCurrentShotTransform(endPosition, endRotation);
        }

        if (teleporterShot.holdDuration > 0f)
            yield return new WaitForSeconds(teleporterShot.holdDuration);

        yield return FadeOutRoutine();
    }

    private void ActivateTeleporterVfx()
    {
        foreach (GameObject vfxObject in teleporterVfxObjects)
            if (vfxObject != null)
                vfxObject.SetActive(true);

        if (teleporterActivateClip == null)
            return;

        if (effectSource == null)
        {
            effectSource = gameObject.AddComponent<AudioSource>();
            effectSource.playOnAwake = false;
            effectSource.spatialBlend = 0f;
        }

        effectSource.volume = teleporterActivateVolume;
        effectSource.PlayOneShot(teleporterActivateClip);
    }

    private void ApplyShotTransform()
    {
        if (xrOrigin == null || xrHead == null)
            return;

        if (followPitch)
            xrOrigin.rotation = currentShotRotation;
        else if (followYaw)
        {
            Vector3 forward = Vector3.ProjectOnPlane(currentShotRotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
                xrOrigin.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
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

    private IEnumerator FadeOutRoutine()
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeDuration));
    }

    private IEnumerator FadeInRoutine()
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

    private void CleanupAfterCutscene()
    {
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CutsceneFinished = null;
    }
}
