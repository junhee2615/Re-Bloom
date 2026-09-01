using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 하나의 컷 = 시점 + 유지 시간.
/// </summary>
[System.Serializable]
public class Stage2CutsceneShot
{
    [Tooltip("이 컷의 시점이 될 Cinemachine 카메라. 위치와 Yaw만 사용한다.")]
    public CinemachineCamera shotCamera;

    [Tooltip("페이드인이 끝난 뒤 이 컷을 유지하는 시간(초).")]
    public float holdDuration = 4f;
}

/// <summary>
/// Stage2 수생식물 미션 완료 연출.
///
/// PlantClearSequence가 세 효과를 한 프레임에 전부 터뜨리던 것을,
/// 컷씬이 각 컷에서 하나씩 발동시키도록 소유권을 가져온다.
///
///   1) SkyCamera  : 페이드인 → 스카이박스 3초 크로스페이드를 "보여준다"
///   2) TreeCamera : 페이드인 → 식생 채도 복원 5초를 "보여준다"
///   3) FishCamera : 검은 화면에서 물고기를 켜고 → 페이드인
///
/// 컷 사이는 검은 페이드로 끊는다. VR에서 시점이 순간이동해도 편안하고,
/// 검은 구간에서 다음 효과를 준비할 수 있어 타이밍도 정확해진다.
///
/// 시점 이동은 Stage1XRCutsceneRigFollower와 같은 방식이다. 카메라가 플레이어를
/// 따라오는 게 아니라, XR Origin을 매 LateUpdate 옮겨 HMD가 컷 카메라 위치에
/// 오도록 맞춘다. 부모 설정도 물리도 없다.
///
/// 재생은 각 피어 로컬에서 일어난다. 트리거(연꽃 복원)가 이미 RPC로 동기화되어
/// 있어 두 피어가 거의 동시에 시작하고, 끝나고 씬 전환도 없어 약간의 드리프트는
/// 문제가 되지 않는다.
/// </summary>
public class Stage2SkyCutscene : MonoBehaviour
{
    [Header("컷 (재생 순서: 하늘 → 식생 → 물고기)")]
    [SerializeField]
    private Stage2CutsceneShot skyShot = new Stage2CutsceneShot { holdDuration = 4.5f };

    [SerializeField]
    private Stage2CutsceneShot treeShot = new Stage2CutsceneShot { holdDuration = 6f };

    [SerializeField]
    private Stage2CutsceneShot fishShot = new Stage2CutsceneShot { holdDuration = 3.5f };

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

    [Tooltip("상하 각도(Pitch)까지 강제한다. HMD와 싸우며 멀미를 유발하므로 권장하지 않는다.")]
    [SerializeField]
    private bool followPitch = false;

    [Header("연출 중 화면 정리")]
    [Tooltip("Resonance 안개/Bloom/Vignette를 끈다.")]
    [SerializeField]
    private bool disableResonance = true;

    [Tooltip("씬 자체의 RenderSettings 안개도 끈다.")]
    [SerializeField]
    private bool disableBuiltinFog = true;

    [Tooltip("컷씬 동안 상대 플레이어의 아바타를 숨긴다. 두 사람이 같은 시점에 겹쳐 있게 되므로 켜두는 편이 좋다.")]
    [SerializeField]
    private bool hideRemoteAvatars = true;

    [Tooltip("컷씬 동안 이동/텔레포트를 잠근다.")]
    [SerializeField]
    private bool lockLocomotion = true;

    [Header("Debug")]
    [SerializeField]
    private bool xrReady;

    [SerializeField]
    private bool isPlaying;

    /// <summary>컷씬이 완전히 끝났을 때 1회.</summary>
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

    private Transform currentShot;

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;

    public bool IsPlaying => isPlaying;

    /// <summary>XR 리그가 준비되어 컷씬을 재생할 수 있는 상태인지.</summary>
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
        if (!isPlaying || currentShot == null)
            return;

        ApplyShotTransform();
    }

    private void ApplyShotTransform()
    {
        if (currentShot == null ||
            xrOrigin == null ||
            xrHead == null)
        {
            return;
        }

        if (followPitch)
        {
            xrOrigin.rotation = currentShot.rotation;
        }
        else if (followYaw)
        {
            Vector3 targetForward =
                Vector3.ProjectOnPlane(
                    currentShot.forward,
                    Vector3.up);

            if (targetForward.sqrMagnitude > 0.001f)
            {
                xrOrigin.rotation =
                    Quaternion.LookRotation(
                        targetForward.normalized,
                        Vector3.up);
            }
        }

        // 실제 HMD 위치를 컷 카메라 위치에 맞춘다.
        xrOrigin.position +=
            currentShot.position - xrHead.position;
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
    /// 재생을 시작했으면 true. false면 호출 측이 기존처럼 즉시 효과를 적용해야 한다.
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

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);

        SetRemoteAvatarsVisible(false);

        SaveOriginalXRTransform();

        // 1. 화면을 검게
        yield return FadeOutRoutine();

        // 2. 검은 상태에서 안개/포스트FX 제거
        if (disableResonance || disableBuiltinFog)
            resonance.Disable(disableBuiltinFog);

        // 안개 색은 지금 바꿔둔다. 안개가 다시 켜질 때 반영된다.
        sequence.ApplyFogColor();

        // 3. 하늘 — 화면이 보인 뒤 스카이박스 전환을 시작해서 "보여준다"
        yield return PlayShot(
            skyShot,
            sequence.StartSkyboxFade,
            false);

        // 4. 식생 — 화면이 보인 뒤 채도 복원을 시작한다
        yield return PlayShot(
            treeShot,
            sequence.ReviveVegetation,
            false);

        // 5. 물고기 — 검은 화면에서 켜고, 이미 있는 상태로 보여준다
        yield return PlayShot(
            fishShot,
            sequence.ActivateObjects,
            true);

        // 6. 복귀 (아직 검은 화면)
        currentShot = null;

        RestoreOriginalXRTransform();

        resonance.Restore();

        SetRemoteAvatarsVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        // 위치/효과 반영에 한 프레임
        yield return null;

        yield return FadeInRoutine();

        isPlaying = false;

        Debug.Log("[Stage2SkyCutscene] 컷씬 종료", this);

        CutsceneFinished?.Invoke();
    }

    /// <summary>
    /// 한 컷: (검은 화면에서) 시점 이동 → 페이드인 → 효과 → 유지 → 페이드아웃.
    /// effectDuringBlack이 true면 효과를 페이드인 전에 발동한다.
    /// </summary>
    private IEnumerator PlayShot(
        Stage2CutsceneShot shot,
        System.Action onShotEffect,
        bool effectDuringBlack)
    {
        if (shot == null || shot.shotCamera == null)
        {
            Debug.LogWarning(
                "[Stage2SkyCutscene] 컷 카메라가 비어 있어 효과만 적용하고 넘어갑니다.",
                this);

            if (onShotEffect != null)
                onShotEffect();

            yield break;
        }

        // 검은 화면에서 시점 이동
        currentShot = shot.shotCamera.transform;
        ApplyShotTransform();

        if (effectDuringBlack && onShotEffect != null)
            onShotEffect();

        if (blackHold > 0f)
            yield return new WaitForSeconds(blackHold);

        yield return FadeInRoutine();

        if (!effectDuringBlack && onShotEffect != null)
            onShotEffect();

        if (shot.holdDuration > 0f)
            yield return new WaitForSeconds(shot.holdDuration);

        yield return FadeOutRoutine();
    }

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
            if (player == null || player.IsLocalNetworkRig)
                continue;

            player.SetAvatarVisible(visible);
        }
    }

    // =================================================
    // 안전장치
    // =================================================

    private void OnDisable()
    {
        if (!isPlaying)
            return;

        StopAllCoroutines();

        isPlaying = false;
        currentShot = null;

        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);
    }

    private void OnDestroy()
    {
        resonance.Restore();
    }
}
