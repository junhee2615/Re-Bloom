using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Fusion;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class Stage1ClearCutscene : NetworkBehaviour
{
    [Header("Video")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("Timing")]
    [SerializeField] private float fadeDuration = 1f;

    [Tooltip("영상이 끝나지 않아도 강제로 진행하는 최대 재생 시간(초).")]
    [SerializeField] private float maxVideoSeconds = 30f;

    [Tooltip("Host가 다른 플레이어의 컷씬 종료 보고를 기다리는 최대 시간.")]
    [SerializeField] private float clientWaitTimeout = 15f;

    [Header("Video UI")]
    [SerializeField] private RawImage videoRawImage;

    [Header("Stage1 Screen Distance")]
    [Tooltip("Stage1 컷씬 동안 ScreenFadeCanvas의 카메라 기준 Z 거리")]
    [SerializeField] private float stage1ScreenDistance = 0.6f;

    private ScreenFade screenFade;
    private Image fadeImage;

    private Camera mainCamera;
    private int originalCullingMask;
    private bool worldRenderingDisabled;

    // ScreenFadeCanvas 거리 임시 변경용
    private Transform screenFadeTransform;
    private Vector3 originalScreenFadeLocalPosition;
    private bool screenDistanceChanged;

    // Resonance 효과 임시 비활성화용
    private ResonanceController resonanceController;
    private bool originalConstraintEnabled;
    private bool resonanceStateSaved;

    private bool cutsceneStarted;

    // Host 전용
    private int reportsReceived;
    private bool finishTriggered;

    public event Action CutsceneFinished;

    [Header("Stage Restore")]
    [SerializeField] private LeverMissionManager leverMissionManager;

    public void BeginCutscene()
    {
        if (!HasStateAuthority || cutsceneStarted)
            return;

        cutsceneStarted = true;
        reportsReceived = 0;
        finishTriggered = false;

        RPC_PlayCutscene();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayCutscene()
    {
        StartCoroutine(CutsceneRoutine());
    }

    private IEnumerator CutsceneRoutine()
    {
        ResolveLocalReferences();

        // 1. 기존 게임 화면 Fade Out
        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeOut(fadeDuration));
        }

        // 2. Stage1 컷씬 동안만 화면 거리를 변경
        ApplyStage1ScreenDistance();

        // 3. 실제 Stage1 월드 렌더링 숨김
        DisableWorldRendering();

        // 4. 맵이 완전히 가려진 뒤 Resonance 효과 제거
        DisableResonanceEffects();

        // 5. 기존 VideoRawImage 사용
        ShowVideo();

        // 6. 영상 준비 및 재생
        yield return StartCoroutine(
            PrepareAndPlayVideo());

        // 7. 검은 Fade Image만 숨김
        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(false);
        }

        // 8. 영상 종료까지 대기
        yield return StartCoroutine(
            WaitForVideoEnd());

        // 9. 다시 검은 화면 표시
        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(true);
        }

        // 10. 영상 숨김
        HideVideo();

        // 11. ScreenFadeCanvas 위치 원상복구
        RestoreScreenDistance();

        // 12. 실제 Stage1 월드 렌더링 복구
        // 아직 검은 화면이라 플레이어에게는 보이지 않음
        RestoreWorldRendering();

        // 13. Host에게 컷씬 완료 보고
        RPC_ReportDone();

        // 14. Host가 실제 맵을 복구하고
        // 그 Networked 상태가 이 클라이언트까지 도착할 때까지
        // 검은 화면을 유지
        yield return StartCoroutine(
            WaitForRestoreApplied());

        // 15. 복구가 완료된 뒤 Resonance 효과 복구
        RestoreResonanceEffects();

        // 16. 이제 복구된 Stage1 화면으로 Fade In
        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeIn(fadeDuration));
        }

        if (HasStateAuthority)
        {
            StartCoroutine(FinishWatchdog());
        }
    }

    private IEnumerator PrepareAndPlayVideo()
    {
        if (videoPlayer == null)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] VideoPlayer가 없습니다.",
                this);

            yield break;
        }

        videoPlayer.Prepare();

        float prepareTime = 0f;

        while (!videoPlayer.isPrepared &&
               prepareTime < 5f)
        {
            prepareTime += Time.deltaTime;
            yield return null;
        }

        if (!videoPlayer.isPrepared)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] VideoPlayer 준비 시간이 초과되었습니다.",
                this);

            yield break;
        }

        videoPlayer.Play();
    }

    private IEnumerator WaitForVideoEnd()
    {
        if (videoPlayer == null)
            yield break;

        bool finished = false;

        void OnVideoEnd(VideoPlayer vp)
        {
            finished = true;
        }

        videoPlayer.loopPointReached += OnVideoEnd;

        float startGuard = 0f;

        while (!videoPlayer.isPlaying &&
               startGuard < 2f)
        {
            startGuard += Time.deltaTime;
            yield return null;
        }

        float elapsed = 0f;

        while (!finished)
        {
            if (maxVideoSeconds > 0f &&
                elapsed >= maxVideoSeconds)
            {
                Debug.LogWarning(
                    "[Stage1ClearCutscene] 최대 영상 재생 시간을 초과하여 컷씬을 종료합니다.",
                    this);

                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        videoPlayer.loopPointReached -= OnVideoEnd;
    }

    private void ShowVideo()
    {
        if (videoRawImage == null)
            return;

        if (videoPlayer != null &&
            videoPlayer.targetTexture != null)
        {
            videoRawImage.texture =
                videoPlayer.targetTexture;
        }

        videoRawImage.gameObject.SetActive(true);
    }

    private void HideVideo()
    {
        if (videoRawImage != null)
        {
            videoRawImage.gameObject.SetActive(false);
        }

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }
    }

    private void ResolveLocalReferences()
    {
        if (screenFade == null)
        {
            screenFade =
                FindFirstObjectByType<ScreenFade>();
        }

        if (screenFade != null)
        {
            // 기존 기차 컷씬과 동일한 VideoRawImage
            if (videoRawImage == null)
            {
                RawImage[] rawImages =
                    screenFade.GetComponentsInChildren<RawImage>(true);

                foreach (RawImage img in rawImages)
                {
                    if (img.name == "VideoRawImage")
                    {
                        videoRawImage = img;
                        break;
                    }
                }
            }

            // 검은 Fade용 Image
            if (fadeImage == null)
            {
                Image[] images =
                    screenFade.GetComponentsInChildren<Image>(true);

                foreach (Image img in images)
                {
                    if (img.name == "Image")
                    {
                        fadeImage = img;
                        break;
                    }
                }
            }

            if (screenFadeTransform == null)
            {
                screenFadeTransform =
                    screenFade.transform;
            }
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (screenFade == null)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] ScreenFade를 찾지 못했습니다.",
                this);
        }

        if (videoRawImage == null)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] VideoRawImage를 찾지 못했습니다.",
                this);
        }

        if (fadeImage == null)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] Fade용 Image를 찾지 못했습니다.",
                this);
        }
    }

    private void ApplyStage1ScreenDistance()
    {
        if (screenFadeTransform == null ||
            screenDistanceChanged)
            return;

        originalScreenFadeLocalPosition =
            screenFadeTransform.localPosition;

        Vector3 position =
            originalScreenFadeLocalPosition;

        position.z =
            stage1ScreenDistance;

        screenFadeTransform.localPosition =
            position;

        screenDistanceChanged = true;
    }

    private void RestoreScreenDistance()
    {
        if (screenFadeTransform == null ||
            !screenDistanceChanged)
            return;

        screenFadeTransform.localPosition =
            originalScreenFadeLocalPosition;

        screenDistanceChanged = false;
    }

    private void DisableWorldRendering()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
            return;

        if (!worldRenderingDisabled)
        {
            originalCullingMask =
                mainCamera.cullingMask;

            worldRenderingDisabled = true;
        }

        int uiLayer =
            LayerMask.NameToLayer("UI");

        if (uiLayer >= 0)
        {
            mainCamera.cullingMask =
                1 << uiLayer;
        }
    }

    private void RestoreWorldRendering()
    {
        if (mainCamera == null ||
            !worldRenderingDisabled)
            return;

        mainCamera.cullingMask =
            originalCullingMask;

        worldRenderingDisabled = false;
    }

    // -------------------------------------------------
    // Resonance Constraint 임시 비활성화
    // ResonanceController 원본 코드는 수정하지 않는다.
    // -------------------------------------------------
    private void DisableResonanceEffects()
    {
        resonanceController =
            ResonanceController.Instance;

        if (resonanceController == null)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] ResonanceController를 찾지 못했습니다.",
                this);

            return;
        }

        BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        FieldInfo constraintField =
            typeof(ResonanceController).GetField(
                "constraintEnabled",
                flags);

        if (constraintField == null)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] constraintEnabled 필드를 찾지 못했습니다.",
                this);

            return;
        }

        originalConstraintEnabled =
            (bool)constraintField.GetValue(
                resonanceController);

        resonanceStateSaved = true;

        // Constraint Enabled OFF
        constraintField.SetValue(
            resonanceController,
            false);

        // Fog 즉시 제거
        FieldInfo fogField =
            typeof(ResonanceController).GetField(
                "fog",
                flags);

        object fogObject =
            fogField?.GetValue(
                resonanceController);

        if (fogObject != null)
        {
            MethodInfo applyMethod =
                fogObject.GetType().GetMethod(
                    "Apply",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            applyMethod?.Invoke(
                fogObject,
                new object[] { 0f });
        }

        // Bloom / Vignette 등 Volume 즉시 완화
        FieldInfo volumeField =
            typeof(ResonanceController).GetField(
                "volume",
                flags);

        object volumeObject =
            volumeField?.GetValue(
                resonanceController);

        if (volumeObject != null)
        {
            MethodInfo snapMethod =
                volumeObject.GetType().GetMethod(
                    "Snap",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            snapMethod?.Invoke(
                volumeObject,
                new object[] { true });

            MethodInfo postFxMethod =
                volumeObject.GetType().GetMethod(
                    "ApplyPostFx",
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            postFxMethod?.Invoke(
                volumeObject,
                new object[] { true });
        }
    }

    private void RestoreResonanceEffects()
    {
        if (resonanceController == null ||
            !resonanceStateSaved)
            return;

        BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        FieldInfo constraintField =
            typeof(ResonanceController).GetField(
                "constraintEnabled",
                flags);

        if (constraintField != null)
        {
            constraintField.SetValue(
                resonanceController,
                originalConstraintEnabled);
        }

        // 현재 공명 상태에 맞게 효과 다시 적용
        MethodInfo applyEffectsMethod =
            typeof(ResonanceController).GetMethod(
                "ApplyConstraintEffects",
                flags);

        applyEffectsMethod?.Invoke(
            resonanceController,
            null);

        resonanceStateSaved = false;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportDone()
    {
        if (!HasStateAuthority)
            return;

        reportsReceived++;

        int expected =
            Runner != null
                ? Runner.ActivePlayers.Count()
                : reportsReceived;

        if (reportsReceived >= expected)
        {
            FinishCutscene();
        }
    }

    private IEnumerator FinishWatchdog()
    {
        yield return new WaitForSeconds(
            clientWaitTimeout);

        FinishCutscene();
    }

    private void FinishCutscene()
    {
        if (!HasStateAuthority ||
            finishTriggered)
            return;

        finishTriggered = true;

        CutsceneFinished?.Invoke();
    }

    private void OnDisable()
    {
        RestoreWorldRendering();
        RestoreScreenDistance();
        RestoreResonanceEffects();

        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(true);
        }
    }

    private IEnumerator WaitForRestoreApplied()
    {
        if (leverMissionManager == null)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] LeverMissionManager가 연결되지 않았습니다.",
                this);

            yield break;
        }

        float timeout = 5f;
        float elapsed = 0f;

        while (!leverMissionManager.IsRestoreApplied &&
            elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!leverMissionManager.IsRestoreApplied)
        {
            Debug.LogWarning(
                "[Stage1ClearCutscene] 복구 상태 동기화 대기 시간이 초과되었습니다.",
                this);
        }
    }
}