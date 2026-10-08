using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Stage3 물길 미션 클리어 컷씬.
/// 카메라 한 대가 시작 지점(shotCamera)에서 다리 쪽(endPoint)으로 이동하면서
/// 물이 차오르는 강바닥을 훑는다.
///
/// Stage2 컷씬과 같은 방식: VR 이라 Cinemachine 이 화면을 직접 잡지 않고,
/// shotCamera(컴포넌트는 꺼 둔 위치 표시용)의 위치를 따라 XR Origin 을 옮긴다.
/// RiverbedFlowController 의 OnMissionComplete 에 Play() 를 연결한다.
/// 각 플레이어가 자기 화면에서 따로 재생한다(네트워크 전송 없음).
/// </summary>
public class Stage3RiverCutscene : MonoBehaviour
{
    [Header("카메라")]
    [Tooltip("컷의 시작 시점. 위치/방향만 쓰므로 CinemachineCamera 컴포넌트는 꺼 둔다")]
    [SerializeField] private CinemachineCamera shotCamera;
    [Tooltip("카메라가 도착할 지점 (다리 쪽)")]
    [SerializeField] private Transform endPoint;
    [Tooltip("시작 지점에서 도착 지점까지 이동하는 시간(초)")]
    [SerializeField] private float moveDuration = 5f;
    [Tooltip("도착한 뒤 그 자리에서 머무는 시간(초)")]
    [SerializeField] private float holdDuration = 1f;
    [SerializeField] private bool smoothCameraMovement = true;
    [SerializeField] private bool followYaw = true;
    [Tooltip("VR 멀미를 줄이려면 꺼 둔다. 끄면 위아래 시선은 플레이어가 직접 움직인다")]
    [SerializeField] private bool followPitch;

    [Header("페이드")]
    [SerializeField] private float fadeDuration = 0.6f;
    [SerializeField] private float blackHold = 0.2f;

    [Header("연출 중 화면 정리")]
    [SerializeField] private bool disableResonance = true;
    [SerializeField] private bool disableBuiltinFog = true;
    [SerializeField] private bool hideRemoteAvatars = true;
    [SerializeField] private bool lockLocomotion = true;
    [SerializeField] private bool hideTutorialCanvas = true;

    [Header("이벤트")]
    [Tooltip("컷씬 화면이 열리는 순간. 물이 차오르는 연출(RisingWater.Play)을 여기에 연결한다")]
    public UnityEvent onShotStarted;
    [Tooltip("컷씬이 끝나고 원래 자리로 돌아온 뒤")]
    public UnityEvent onCutsceneFinished;

    [Header("Debug")]
    [SerializeField] private bool xrReady;
    [SerializeField] private bool isPlaying;

    private Camera runtimeCamera;
    private Transform xrHead;
    private Transform xrOrigin;
    private ScreenFade screenFade;
    private HardwareRig hardwareRig;
    private bool pendingPlay;
    private bool hasPlayed;
    private bool shotStartedFired;

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;

    private Vector3 currentShotPosition;
    private Quaternion currentShotRotation;
    private bool hasCurrentShot;

    private readonly ResonanceCutsceneOverride resonance = new ResonanceCutsceneOverride();

    public bool IsPlaying => isPlaying;

    private IEnumerator Start()
    {
        // XR 카메라는 방 입장 후 생성되므로 기다린다
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
            Debug.LogError("[Stage3RiverCutscene] XR Origin을 찾지 못했습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        xrReady = true;

        if (pendingPlay)
        {
            pendingPlay = false;
            Play();
        }
    }

    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }

    /// <summary>미션 클리어에서 호출. OnMissionComplete 에 연결한다.</summary>
    public void Play()
    {
        if (hasPlayed || isPlaying)
            return;

        if (!xrReady)
        {
            pendingPlay = true;
            return;
        }

        if (shotCamera == null)
        {
            // 카메라가 없으면 컷씬 없이 물만 차오르게 한다
            Debug.LogWarning("[Stage3RiverCutscene] Shot Camera가 비어 있어 컷씬을 건너뜁니다.", this);
            hasPlayed = true;
            FireShotStarted();
            onCutsceneFinished?.Invoke();
            return;
        }

        hasPlayed = true;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        isPlaying = true;
        SaveOriginalXRTransform();

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);
        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        // 1. 게임 화면 -> 검정
        yield return FadeOutRoutine();
        if (disableResonance || disableBuiltinFog)
            resonance.Disable(disableBuiltinFog);

        // 2. 검은 화면에서 시작 지점으로 이동
        Vector3 startPosition = shotCamera.transform.position;
        Quaternion startRotation = shotCamera.transform.rotation;
        SetCurrentShotTransform(startPosition, startRotation);

        if (blackHold > 0f)
            yield return new WaitForSeconds(blackHold);

        // 3. 화면이 열리면서 물이 차오르기 시작
        FireShotStarted();
        yield return FadeInRoutine();

        // 4. 다리 쪽으로 이동하며 강바닥을 훑는다
        if (endPoint != null && moveDuration > 0f)
        {
            Vector3 endPosition = endPoint.position;
            Quaternion endRotation = endPoint.rotation;
            float elapsed = 0f;
            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveDuration);
                if (smoothCameraMovement)
                    t = Mathf.SmoothStep(0f, 1f, t);
                SetCurrentShotTransform(
                    Vector3.Lerp(startPosition, endPosition, t),
                    Quaternion.Slerp(startRotation, endRotation, t));
                yield return null;
            }
            SetCurrentShotTransform(endPosition, endRotation);
        }

        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        // 5. 검정 -> 원래 자리로 복귀 -> 게임 화면
        yield return FadeOutRoutine();

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

        onCutsceneFinished?.Invoke();
    }

    private void FireShotStarted()
    {
        if (shotStartedFired)
            return;
        shotStartedFired = true;
        onShotStarted?.Invoke();
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

        // 실제 HMD 위치를 컷씬 카메라 위치에 맞춘다
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

    private void OnDisable()
    {
        StopAllCoroutines();

        if (!isPlaying)
            return;

        // 컷씬 도중 꺼지면 원래 상태로 되돌리고, 물은 반드시 차오르게 한다
        isPlaying = false;
        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);
        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);
        FireShotStarted();
    }
}
