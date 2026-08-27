using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Playables;

public class Stage1XRCutsceneRigFollower : MonoBehaviour
{
    [Header("Cutscene")]
    [SerializeField]
    private PlayableDirector playableDirector;

    [Tooltip("Cinemachine Brain이 움직이는 Stage1CutsceneCamera")]
    [SerializeField]
    private Transform cutsceneCamera;

    [Header("Runtime XR - 자동 연결")]
    [SerializeField]
    private Transform xrOrigin;

    [SerializeField]
    private Transform xrHead;

    [Header("Follow Settings")]
    [SerializeField]
    private bool followPosition = true;

    [SerializeField]
    private bool followYaw = true;

    [Header("Cutscene Black Fade - 60fps 기준")]
    [Tooltip("컷씬 시작/종료 시 검정 Fade에 사용할 프레임 수")]
    [SerializeField]
    private int cutsceneFadeFrames = 60;

    [Header("Restore Flash - 60fps 기준")]
    [Tooltip("흰색 플래시가 가장 밝아지는 프레임")]
    [SerializeField]
    private int flashPeakFrame = 230;

    [Tooltip("플래시가 밝아지는 데 걸리는 프레임")]
    [SerializeField]
    private int flashFadeInFrames = 40;

    [Tooltip("플래시가 사라지는 데 걸리는 프레임")]
    [SerializeField]
    private int flashFadeOutFrames = 50;

    [SerializeField]
    private float timelineFrameRate = 60f;

    [Header("Debug")]
    [SerializeField]
    private bool xrReady;

    [SerializeField]
    private bool cutsceneActive;

    [SerializeField]
    private bool flashPlayed;

    [SerializeField]
    private bool waitingForCutsceneStart;

    [SerializeField]
    private bool finishingCutscene;

    [SerializeField]
    private bool endFadeStarted;

    [SerializeField]
    private bool endFadeCompleted;

    private Camera runtimeCamera;

    // =================================================
    // Screen Fade
    // =================================================

    private ScreenFade screenFade;

    private Coroutine endFadeCoroutine;

    // =================================================
    // Player Position Restore
    // =================================================

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;

    private bool originalXRTransformSaved;

    // =================================================
    // Restore Flash
    // =================================================

    private CanvasGroup restoreFlashCanvasGroup;
    private Coroutine flashCoroutine;

    // =================================================
    // Resonance
    // =================================================

    private ResonanceController resonanceController;

    private FieldInfo constraintEnabledField;
    private FieldInfo fogField;
    private FieldInfo volumeField;

    private bool originalConstraintEnabled;
    private bool resonanceStateSaved;

    // =================================================
    // Event
    // =================================================

    public event Action CutsceneFinished;

    // =================================================
    // Start
    // =================================================

    private IEnumerator Start()
    {
        if (playableDirector != null)
        {
            playableDirector.played +=
                OnDirectorPlayed;

            playableDirector.stopped +=
                OnDirectorStopped;
        }
        else
        {
            Debug.LogError(
                "[Stage1XRCutsceneRigFollower] " +
                "PlayableDirector가 연결되지 않았습니다.",
                this);
        }

        if (cutsceneCamera == null)
        {
            Debug.LogError(
                "[Stage1XRCutsceneRigFollower] " +
                "Stage1CutsceneCamera가 연결되지 않았습니다.",
                this);
        }

        ResolveResonanceController();

        // XR Main Camera가 생성될 때까지 대기
        while (runtimeCamera == null)
        {
            runtimeCamera = Camera.main;
            yield return null;
        }

        xrHead = runtimeCamera.transform;

        // =================================================
        // XR Origin 찾기
        // =================================================

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
                "[Stage1XRCutsceneRigFollower] " +
                "XR Origin을 찾지 못했습니다.",
                this);

            yield break;
        }

        // =================================================
        // Restore Flash 찾기
        // =================================================

        ResolveFlash();

        // =================================================
        // 기존 ScreenFade 찾기
        // =================================================

        screenFade =
            runtimeCamera.GetComponentInChildren
                <ScreenFade>(true);

        if (screenFade == null)
        {
            Debug.LogWarning(
                "[Stage1XRCutsceneRigFollower] " +
                "ScreenFade를 찾지 못했습니다.",
                this);
        }

        xrReady = true;

        Debug.Log(
            $"[Stage1XRCutsceneRigFollower] XR 연결 완료 - " +
            $"Origin: {xrOrigin.name}, " +
            $"Head: {xrHead.name}",
            this);
    }

    // =================================================
    // Update
    // =================================================

    private void LateUpdate()
    {
        if (!cutsceneActive ||
            !xrReady)
        {
            return;
        }

        FollowCutsceneCamera();

        CheckRestoreFlash();

        // Timeline 마지막 부분에서
        // 미리 검정 Fade 시작
        CheckEndFade();
    }

    // =================================================
    // XR Position Save / Restore
    // =================================================

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

        originalXRTransformSaved = true;

        Debug.Log(
            "[Stage1XRCutsceneRigFollower] " +
            "컷씬 시작 위치 저장",
            this);
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

        originalXRTransformSaved = false;

        Debug.Log(
            "[Stage1XRCutsceneRigFollower] " +
            "컷씬 시작 위치로 복귀",
            this);
    }

    // =================================================
    // Cinemachine -> XR Rig
    // =================================================

    private void FollowCutsceneCamera()
    {
        if (cutsceneCamera == null ||
            xrOrigin == null ||
            xrHead == null)
        {
            return;
        }

        // Cinemachine Camera의 Yaw 적용
        if (followYaw)
        {
            Vector3 targetForward =
                Vector3.ProjectOnPlane(
                    cutsceneCamera.forward,
                    Vector3.up);

            if (targetForward.sqrMagnitude >
                0.001f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(
                        targetForward.normalized,
                        Vector3.up);

                xrOrigin.rotation =
                    targetRotation;
            }
        }

        // 실제 HMD 위치를
        // Cinemachine Camera 위치에 맞춤
        if (followPosition)
        {
            Vector3 positionOffset =
                cutsceneCamera.position -
                xrHead.position;

            xrOrigin.position +=
                positionOffset;
        }
    }

    // =================================================
    // Restore Flash
    // =================================================

    private void ResolveFlash()
    {
        if (runtimeCamera == null)
            return;

        CanvasGroup[] canvasGroups =
            runtimeCamera.GetComponentsInChildren
                <CanvasGroup>(true);

        foreach (CanvasGroup group in canvasGroups)
        {
            if (group.gameObject.name ==
                "Stage1RestoreFlash")
            {
                restoreFlashCanvasGroup =
                    group;

                break;
            }
        }

        if (restoreFlashCanvasGroup == null)
        {
            Debug.LogWarning(
                "[Stage1XRCutsceneRigFollower] " +
                "Stage1RestoreFlash CanvasGroup을 " +
                "찾지 못했습니다.",
                this);

            return;
        }

        restoreFlashCanvasGroup.alpha = 0f;

        Debug.Log(
            "[Stage1XRCutsceneRigFollower] " +
            "Stage1 Restore Flash 연결 완료",
            this);
    }

    private void CheckRestoreFlash()
    {
        if (flashPlayed ||
            restoreFlashCanvasGroup == null ||
            playableDirector == null)
        {
            return;
        }

        double currentFrame =
            playableDirector.time *
            timelineFrameRate;

        int flashStartFrame =
            flashPeakFrame -
            flashFadeInFrames;

        if (currentFrame >=
            flashStartFrame)
        {
            flashPlayed = true;

            if (flashCoroutine != null)
            {
                StopCoroutine(
                    flashCoroutine);
            }

            flashCoroutine =
                StartCoroutine(
                    PlayRestoreFlash());
        }
    }

    private IEnumerator PlayRestoreFlash()
    {
        float fadeInDuration =
            flashFadeInFrames /
            timelineFrameRate;

        float fadeOutDuration =
            flashFadeOutFrames /
            timelineFrameRate;

        // =================================================
        // 투명 -> 흰색
        // =================================================

        float elapsed = 0f;

        while (elapsed <
               fadeInDuration)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    fadeInDuration);

            restoreFlashCanvasGroup.alpha =
                Mathf.Lerp(
                    0f,
                    1f,
                    t);

            yield return null;
        }

        restoreFlashCanvasGroup.alpha = 1f;

        // =================================================
        // 흰색 -> 투명
        // =================================================

        elapsed = 0f;

        while (elapsed <
               fadeOutDuration)
        {
            elapsed +=
                Time.deltaTime;

            float t =
                Mathf.Clamp01(
                    elapsed /
                    fadeOutDuration);

            restoreFlashCanvasGroup.alpha =
                Mathf.Lerp(
                    1f,
                    0f,
                    t);

            yield return null;
        }

        restoreFlashCanvasGroup.alpha = 0f;

        flashCoroutine = null;
    }

    private void StopFlash()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(
                flashCoroutine);

            flashCoroutine = null;
        }

        if (restoreFlashCanvasGroup != null)
        {
            restoreFlashCanvasGroup.alpha = 0f;
        }
    }

    // =================================================
    // End Fade Detection
    // =================================================

    private void CheckEndFade()
    {
        if (endFadeStarted ||
            playableDirector == null ||
            screenFade == null)
        {
            return;
        }

        double currentFrame =
            playableDirector.time *
            timelineFrameRate;

        double totalFrames =
            playableDirector.duration *
            timelineFrameRate;

        double fadeStartFrame =
            totalFrames -
            cutsceneFadeFrames;

        // Timeline 종료 N프레임 전부터
        // 검정 Fade 시작
        if (currentFrame >=
            fadeStartFrame)
        {
            endFadeStarted = true;
            endFadeCompleted = false;

            endFadeCoroutine =
                StartCoroutine(
                    EndFadeBeforeTimelineFinish());
        }
    }

    private IEnumerator EndFadeBeforeTimelineFinish()
    {
        float fadeDuration =
            cutsceneFadeFrames /
            timelineFrameRate;

        // Timeline이 계속 재생되는 동안
        // 화면만 점점 검게 만든다.
        yield return StartCoroutine(
            screenFade.FadeOut(
                fadeDuration));

        // 정상적인 경우 여기서
        // Timeline도 거의 마지막 프레임에 도달
        endFadeCompleted = true;

        endFadeCoroutine = null;
    }

    // =================================================
    // Cutscene Start
    // =================================================

    public void PlayCutscene()
    {
        if (playableDirector == null ||
            !xrReady ||
            cutsceneActive ||
            waitingForCutsceneStart ||
            finishingCutscene)
        {
            return;
        }

        StartCoroutine(
            StartCutsceneWithFade());
    }

    private IEnumerator StartCutsceneWithFade()
    {
        waitingForCutsceneStart = true;

        // =================================================
        // 1. 현재 플레이어 위치 저장
        // =================================================

        SaveOriginalXRTransform();

        float fadeDuration =
            cutsceneFadeFrames /
            timelineFrameRate;

        // =================================================
        // 2. 게임 화면 -> 검정
        // =================================================

        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeOut(
                    fadeDuration));
        }

        // =================================================
        // 3. 검은 상태에서 Resonance 제거
        // =================================================

        DisableResonanceConstraint();

        // Flash 초기화
        if (restoreFlashCanvasGroup == null)
        {
            ResolveFlash();
        }

        StopFlash();

        flashPlayed = false;

        // 종료 Fade 상태 초기화
        ResetEndFadeState();

        // =================================================
        // 4. Timeline 시작
        // =================================================

        playableDirector.time = 0;
        playableDirector.Play();

        // OnDirectorPlayed 실행 대기
        yield return null;

        // XR Rig가 첫 Cinemachine 위치로 이동할 시간 확보
        yield return new WaitForEndOfFrame();

        // =================================================
        // 5. 검정 -> 컷씬 화면
        // =================================================

        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeIn(
                    fadeDuration));
        }

        waitingForCutsceneStart = false;

        Debug.Log(
            "[Stage1XRCutsceneRigFollower] " +
            "컷씬 시작 Fade 완료",
            this);
    }

    // =================================================
    // Timeline Played
    // =================================================

    private void OnDirectorPlayed(
        PlayableDirector director)
    {
        if (!xrReady)
        {
            Debug.LogWarning(
                "[Stage1XRCutsceneRigFollower] " +
                "XR Rig가 아직 준비되지 않았습니다.",
                this);

            return;
        }

        // 수동 Timeline Play에도 대응
        if (!originalXRTransformSaved)
        {
            SaveOriginalXRTransform();
        }

        DisableResonanceConstraint();

        if (restoreFlashCanvasGroup == null)
        {
            ResolveFlash();
        }

        StopFlash();

        flashPlayed = false;

        ResetEndFadeState();

        cutsceneActive = true;

        Debug.Log(
            "[Stage1XRCutsceneRigFollower] " +
            "실시간 VR 컷씬 시작",
            this);
    }

    // =================================================
    // Timeline Stop
    // =================================================

    private void OnDirectorStopped(
        PlayableDirector director)
    {
        if (finishingCutscene)
            return;

        cutsceneActive = false;

        StartCoroutine(
            FinishCutsceneWithFade());
    }

    // =================================================
    // Cutscene End
    // =================================================

    private IEnumerator FinishCutsceneWithFade()
    {
        finishingCutscene = true;

        float fadeDuration =
            cutsceneFadeFrames /
            timelineFrameRate;

        // =================================================
        // 1. 정상 종료
        //
        // Timeline 마지막 N프레임 동안
        // 이미 FadeOut이 진행되고 있음.
        // =================================================

        if (endFadeStarted)
        {
            // Timeline이 아주 조금 먼저 끝난 경우
            // Fade가 완전히 검정이 될 때까지 기다림
            while (!endFadeCompleted)
            {
                yield return null;
            }
        }
        else
        {
            // =================================================
            // Timeline을 중간에 Stop한 경우 등의 안전장치
            // =================================================

            if (screenFade != null)
            {
                yield return StartCoroutine(
                    screenFade.FadeOut(
                        fadeDuration));
            }
        }

        // =================================================
        // 2. 이제 화면은 완전 검정
        // =================================================

        StopFlash();

        // =================================================
        // 3. 원래 플레이어 위치 복귀
        // =================================================

        RestoreOriginalXRTransform();

        // =================================================
        // 4. Resonance 원래 상태 복구
        // =================================================

        RestoreResonanceConstraint();

        // 위치/효과 적용 시간 확보
        yield return null;

        // =================================================
        // 5. 검정 -> 원래 게임 화면
        // =================================================

        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeIn(
                    fadeDuration));
        }

        // =================================================
        // 6. 종료 상태 정리
        // =================================================

        ResetEndFadeState();

        finishingCutscene = false;
        waitingForCutsceneStart = false;

        Debug.Log(
            "[Stage1XRCutsceneRigFollower] " +
            "컷씬 종료 Fade 및 위치 복귀 완료",
            this);

        // 로컬 컷씬 처리가 완전히 끝난 뒤
        // LeverMissionManager에게 완료 보고
        CutsceneFinished?.Invoke();
    }

    // =================================================
    // End Fade Reset
    // =================================================

    private void ResetEndFadeState()
    {
        if (endFadeCoroutine != null)
        {
            StopCoroutine(
                endFadeCoroutine);

            endFadeCoroutine = null;
        }

        endFadeStarted = false;
        endFadeCompleted = false;
    }

    // =================================================
    // Resonance
    // =================================================

    private void ResolveResonanceController()
    {
        if (resonanceController != null)
            return;

        resonanceController =
            FindFirstObjectByType
                <ResonanceController>();

        if (resonanceController == null)
        {
            Debug.LogWarning(
                "[Stage1XRCutsceneRigFollower] " +
                "ResonanceController를 찾지 못했습니다.",
                this);

            return;
        }

        BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        constraintEnabledField =
            typeof(ResonanceController)
                .GetField(
                    "constraintEnabled",
                    flags);

        fogField =
            typeof(ResonanceController)
                .GetField(
                    "fog",
                    flags);

        volumeField =
            typeof(ResonanceController)
                .GetField(
                    "volume",
                    flags);
    }

    private void DisableResonanceConstraint()
    {
        ResolveResonanceController();

        if (resonanceController == null ||
            constraintEnabledField == null)
        {
            return;
        }

        if (!resonanceStateSaved)
        {
            originalConstraintEnabled =
                (bool)
                constraintEnabledField
                    .GetValue(
                        resonanceController);

            resonanceStateSaved = true;
        }

        // Constraint OFF
        constraintEnabledField.SetValue(
            resonanceController,
            false);

        BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        // =================================================
        // Fog 제거
        // =================================================

        object fog =
            fogField?.GetValue(
                resonanceController);

        if (fog != null)
        {
            MethodInfo applyMethod =
                fog.GetType()
                    .GetMethod(
                        "Apply",
                        flags);

            applyMethod?.Invoke(
                fog,
                new object[] { 0f });
        }

        // =================================================
        // Post Processing 완화
        // =================================================

        object volume =
            volumeField?.GetValue(
                resonanceController);

        if (volume != null)
        {
            MethodInfo snapMethod =
                volume.GetType()
                    .GetMethod(
                        "Snap",
                        flags);

            snapMethod?.Invoke(
                volume,
                new object[] { true });

            MethodInfo postFxMethod =
                volume.GetType()
                    .GetMethod(
                        "ApplyPostFx",
                        flags);

            postFxMethod?.Invoke(
                volume,
                new object[] { true });
        }
    }

    private void RestoreResonanceConstraint()
    {
        if (resonanceController == null ||
            constraintEnabledField == null ||
            !resonanceStateSaved)
        {
            return;
        }

        BindingFlags flags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        constraintEnabledField.SetValue(
            resonanceController,
            originalConstraintEnabled);

        MethodInfo applyEffectsMethod =
            typeof(ResonanceController)
                .GetMethod(
                    "ApplyConstraintEffects",
                    flags);

        applyEffectsMethod?.Invoke(
            resonanceController,
            null);

        resonanceStateSaved = false;
    }

    // =================================================
    // Manual Stop
    // =================================================

    public void StopCutscene()
    {
        if (playableDirector == null)
            return;

        playableDirector.Stop();
    }

    // =================================================
    // State
    // =================================================

    public bool IsReady =>
        xrReady;

    public bool IsPlaying =>
        cutsceneActive ||
        waitingForCutsceneStart ||
        finishingCutscene;

    // =================================================
    // Safety
    // =================================================

    private void OnDisable()
    {
        cutsceneActive = false;
        waitingForCutsceneStart = false;
        finishingCutscene = false;

        StopAllCoroutines();

        flashCoroutine = null;
        endFadeCoroutine = null;

        if (restoreFlashCanvasGroup != null)
        {
            restoreFlashCanvasGroup.alpha = 0f;
        }

        endFadeStarted = false;
        endFadeCompleted = false;

        RestoreOriginalXRTransform();
        RestoreResonanceConstraint();
    }

    private void OnDestroy()
    {
        if (playableDirector != null)
        {
            playableDirector.played -=
                OnDirectorPlayed;

            playableDirector.stopped -=
                OnDirectorStopped;
        }

        StopFlash();
        ResetEndFadeState();

        RestoreOriginalXRTransform();
        RestoreResonanceConstraint();
    }
}