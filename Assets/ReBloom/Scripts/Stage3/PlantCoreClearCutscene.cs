using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mission 3-2 식물 코어 씨앗 성장 관찰 컷씬.
/// 식물 코어 정면에서 씨앗 낙하 → 땅속 흡수 → 새싹 성장을 가까이서 관찰.
///
/// XR Origin 기반 위치 조정 — Main Camera 직접 이동 금지.
/// WaterCoreClearCutscene 구조를 참고한 직선 이동 방식.
/// 이동 중 XR Origin 회전 없음 — HMD 헤드 트래킹 유지.
/// </summary>
[AddComponentMenu("ReBloom/Stage3/Plant Core Clear Cutscene")]
public class PlantCoreClearCutscene : MonoBehaviour
{
    [Header("카메라 경로")]
    [Tooltip("씨앗 배출구와 새싹이 모두 보이는 시작 위치(Head Position).")]
    [SerializeField] private Transform startShot;
    [Tooltip("전진 후 최종 카메라 위치. StartShot에서 0.8m 전진한 지점.")]
    [SerializeField] private Transform endShot;

    [Header("타이밍")]
    [SerializeField] private float fadeDuration = 0.5f;
    [Tooltip("FadeIn 후 씨앗 애니메이션 시작 전 대기(초).")]
    [SerializeField] private float startHoldDuration = 0f;
    [Tooltip("카메라 전진 시간(초). drop(0.74)+wait(0.5)+sink(1)+grow(2) = 4.24s.")]
    [SerializeField] private float moveDuration = 4.24f;
    [Tooltip("새싹 성장 완료 후 유지 시간(초).")]
    [SerializeField] private float endHoldDuration = 1.5f;

    [Header("식물 미리보기 연출")]
    [Tooltip("PlantCoreSeedButton 참조. 비워두면 씬에서 자동 검색.")]
    [SerializeField] private PlantCoreSeedButton seedButton;
    [Tooltip("씨앗 버튼 트리거 후 카메라 전진 시작까지 대기(초).\n" +
             "0 = 씨앗 낙하와 동시에 카메라 전진 시작. moveDuration이 전체 애니메이션 시간과 맞춤.")]
    [SerializeField] private float cameraMovementDelay = 0f;
    [Tooltip("씨앗 흡수 및 새싹 성장 파티클 컴포넌트.")]
    [SerializeField] private PlantCoreParticle plantParticle;

    [Header("효과음")]
    [Tooltip("새싹 성장 시 한 번 재생할 효과음 (alive_decal 등).")]
    [SerializeField] private AudioClip sproutGrowClip;
    [Tooltip("새싹 성장 효과음 볼륨.")]
    [SerializeField, Range(0f, 1f)] private float sproutGrowVolume = 0.7f;

    [Header("이동 설정")]
    [Tooltip("전체 이동 가속/감속 곡선. X=시간(0-1), Y=경로 진행(0-1).")]
    [SerializeField] private AnimationCurve movementEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("StartShot 회전에 추가되는 Yaw 오프셋(도). " +
             "Configure 메뉴 미실행 시 180으로 기계를 정면에 오도록 보정. " +
             "Configure 재실행 후 0으로 복원됨.")]
    [SerializeField] private float startYawOffsetDeg = 180f;

    [Header("연출 중 화면 정리")]
    [SerializeField] private bool disableResonance = true;
    [SerializeField] private bool disableBuiltinFog = true;
    [SerializeField] private bool hideRemoteAvatars = true;
    [SerializeField] private bool lockLocomotion = true;
    [SerializeField] private bool hideTutorialCanvas = true;

    [Header("Debug")]
    [SerializeField] private bool xrReady;
    [SerializeField] private bool isPlaying;

    public bool IsPlaying => isPlaying;

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

    private AudioSource _effectSource; // 새싹 성장 효과음용 (2D)

    // ── 미리보기 (XR 없는 에디터) ────────────────────────────
    private bool previewMode;
    private Camera previewCamera;
    private CanvasGroup previewFade;

    // ── 식물 미리보기 상태 복원 ──────────────────────────────
    private PlantPreviewState? savedPlantState;

    private struct PlantPreviewState
    {
        public Vector3 seedLocalPosition;
        public bool sproutsWasActive;
        public Vector3[] sproutChildScales;
        public bool sequenceStarted;
    }

    // PlantCoreSeedButton private 필드 — 미리보기 복원 및 파티클 타이밍에 사용
    private static readonly FieldInfo _seedsField =
        typeof(PlantCoreSeedButton).GetField(
            "seeds", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo _sproutsField =
        typeof(PlantCoreSeedButton).GetField(
            "sprouts", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo _seqStartedField =
        typeof(PlantCoreSeedButton).GetField(
            "sequenceStarted", BindingFlags.NonPublic | BindingFlags.Instance);
    // 씨앗 착지 AudioSource — 복원 시 중단용
    private static readonly FieldInfo _seedDropAudioField =
        typeof(PlantCoreSeedButton).GetField(
            "seedDropAudio", BindingFlags.NonPublic | BindingFlags.Instance);
    // 파티클 타이밍 계산용
    private static readonly FieldInfo _dropDurField =
        typeof(PlantCoreSeedButton).GetField(
            "dropDuration", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo _waitAfterDropField =
        typeof(PlantCoreSeedButton).GetField(
            "waitAfterDrop", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo _sinkDurField =
        typeof(PlantCoreSeedButton).GetField(
            "sinkDuration", BindingFlags.NonPublic | BindingFlags.Instance);
    // Press() 대신 StartSequence()를 직접 호출 — 네트워크/권한 경로를 우회
    private static readonly MethodInfo _startSeqMethod =
        typeof(PlantCoreSeedButton).GetMethod(
            "StartSequence", BindingFlags.NonPublic | BindingFlags.Instance);

    // ── 초기화 ───────────────────────────────────────────────

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
            Debug.LogError(
                "[PlantCoreClearCutscene] XR Origin을 찾지 못했습니다. " +
                "Camera.main의 부모 계층에 'XR Origin'이 없습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        xrReady = true;

        if (seedButton == null)
            seedButton = FindFirstObjectByType<PlantCoreSeedButton>(
                FindObjectsInactive.Include);

        // 미션 완료 이벤트 구독 — IsPressed 변경 시 컷씬이 먼저 실행된 뒤 씨앗 시퀀스를 트리거한다
        if (seedButton != null)
        {
            seedButton.OnPressed -= OnMissionCleared; // 중복 방지
            seedButton.OnPressed += OnMissionCleared;
        }

        Debug.Log(
            $"[PlantCoreClearCutscene] XR 초기화 완료\n" +
            $"  xrHead: {xrHead.name}\n" +
            $"  xrOrigin: {xrOrigin.name}\n" +
            $"  screenFade: {(screenFade != null ? screenFade.name : "없음")}\n" +
            $"  startShot: {(startShot != null ? startShot.name : "null ← Inspector 미연결!")}\n" +
            $"  endShot: {(endShot != null ? endShot.name : "null ← Inspector 미연결!")}\n" +
            $"  seedButton: {(seedButton != null ? seedButton.name + " (OnPressed 연결 완료)" : "null — 미션 연결 불가")}",
            this);

        if (pendingPlay)
        {
            pendingPlay = false;
            TryPlay(false);
        }
    }

    // ── 실제 미션 연결 ───────────────────────────────────────

    private void OnMissionCleared()
    {
        if (isPlaying) return; // 중복 재생 방지
        if (!TryPlay(false) && xrReady)
        {
            // XR 준비됐지만 컷씬 시작 불가(예: startShot 미연결) — 씨앗 직접 실행으로 미션 차단 방지
            _startSeqMethod?.Invoke(seedButton, null);
        }
        // !xrReady면 pendingPlay=true로 대기 → Start() 완료 후 자동 재시도
    }

    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }

    // ── 외부 호출 API ────────────────────────────────────────

    public bool TryPlay() => TryPlay(false);

    private bool TryPlay(bool preview)
    {
        if (isPlaying) return false;

        if (!xrReady && !preview)
        {
            pendingPlay = true;
            return false;
        }

        previewMode = preview;
        StartCoroutine(PlayRoutine(preview));
        return true;
    }

    // ── 메인 연출 흐름 ───────────────────────────────────────

    private IEnumerator PlayRoutine(bool preview)
    {
        isPlaying = true;
        SaveOriginalXRTransform();

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);
        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        // 1. FadeOut
        yield return FadeOutRoutine();

        if (disableResonance || disableBuiltinFog)
            resonance.Disable(disableBuiltinFog);

        // 2. 시작 위치로 이동 — Fade Out 중이므로 회전이 보이지 않음
        if (startShot != null)
        {
            Quaternion startRot = FlattenYaw(startShot.rotation)
                * Quaternion.Euler(0f, startYawOffsetDeg, 0f);
            SetCurrentShotTransform(startShot.position, startRot);
        }
        else
        {
            Debug.LogError(
                "[PlantCoreClearCutscene] startShot이 null — Inspector에서 " +
                "PlantCoreClearStartShot Transform을 연결하세요.", this);
        }

        // 3. FadeIn
        yield return FadeInRoutine();

        // 4. 시작 위치 Hold
        if (startHoldDuration > 0f)
            yield return new WaitForSeconds(startHoldDuration);

        // 5. 씨앗 애니메이션 트리거 (미리보기 & 실제 미션 공통)
        //    미리보기 시에만 상태를 저장해 종료 후 복원하고, 실제 미션 시에는 최종 상태를 유지한다
        if (seedButton != null)
        {
            if (preview) savedPlantState = CapturePlantState();
            TriggerSeedButton();
            if (plantParticle != null)
                StartCoroutine(ParticleRoutine());
            if (cameraMovementDelay > 0f)
                yield return new WaitForSeconds(cameraMovementDelay);
        }

        // 6. 카메라 전진 (직선, 고정 회전 — HMD 헤드 트래킹 유지)
        yield return AnimateCameraPath();

        // 7. 최종 위치 Hold
        if (endHoldDuration > 0f)
            yield return new WaitForSeconds(endHoldDuration);

        // 8. FadeOut
        yield return FadeOutRoutine();

        // 9. 복원
        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        // 미리보기 식물 상태 복원
        if (preview && savedPlantState.HasValue)
        {
            RestorePlantState(savedPlantState.Value);
            savedPlantState = null;
        }

        yield return null;

        // 10. FadeIn
        yield return FadeInRoutine();

        isPlaying = false;
    }

    // ── 효과음 ───────────────────────────────────────────────

    private void PlaySproutGrowSound()
    {
        if (sproutGrowClip == null)
        {
            Debug.LogWarning("[PlantCoreClearCutscene] sproutGrowClip이 null — ReBloom/Setup/Setup Plant Core Audio 메뉴를 실행하세요.", this);
            return;
        }
        var src = EnsureEffectSource();
        if (src.isPlaying) src.Stop();
        src.PlayOneShot(sproutGrowClip, sproutGrowVolume);
    }

    private AudioSource EnsureEffectSource()
    {
        if (_effectSource != null) return _effectSource;
        // GetComponent를 사용하지 않음 — 기존 AudioSource가 volume=0이거나
        // AudioMixerGroup 설정으로 무음이 될 수 있으므로 항상 새 컴포넌트 생성
        _effectSource = gameObject.AddComponent<AudioSource>();
        _effectSource.playOnAwake          = false;
        _effectSource.loop                 = false;
        _effectSource.spatialBlend         = 0f;
        _effectSource.volume               = 1f;
        _effectSource.outputAudioMixerGroup = null;
        return _effectSource;
    }

    // ── 파티클 타이밍 루틴 (카메라 이동과 병렬 실행) ─────────

    private IEnumerator ParticleRoutine()
    {
        if (plantParticle == null || seedButton == null) yield break;

        float drop = _dropDurField?.GetValue(seedButton) is float d ? d : 0.74f;
        float wait = _waitAfterDropField?.GetValue(seedButton) is float w ? w : 0.5f;
        float sink = _sinkDurField?.GetValue(seedButton) is float s ? s : 1f;

        // 씨앗이 땅으로 가라앉기 시작할 때까지 대기 (낙하+정지 구간)
        float sinkStart = drop + wait;
        if (sinkStart > 0f)
            yield return new WaitForSeconds(sinkStart);

        // 씨앗 흡수 파티클 — seeds Transform 위치에서 발생
        Transform seeds = _seedsField?.GetValue(seedButton) as Transform;
        if (seeds != null)
            plantParticle.PlaySeedSink(new[] { seeds.position });

        // sinkDuration 후 새싹 성장 파티클 시작
        if (sink > 0f)
            yield return new WaitForSeconds(sink);

        // 새싹 성장 효과음 — 전체에서 한 번만 재생
        PlaySproutGrowSound();

        // 새싹 성장 파티클 — 각 새싹 자식 위치에서 발생
        GameObject sprouts = _sproutsField?.GetValue(seedButton) as GameObject;
        if (sprouts != null && sprouts.transform.childCount > 0)
        {
            var positions = new List<Vector3>();
            for (int i = 0; i < sprouts.transform.childCount; i++)
                positions.Add(sprouts.transform.GetChild(i).position);
            plantParticle.PlaySproutGrow(positions);
        }
        // 새싹 파티클은 루프 — RestorePlantState에서 StopAll(clear)로 정리
    }

    // ── 카메라 직선 이동 (회전 고정) ─────────────────────────

    private IEnumerator AnimateCameraPath()
    {
        if (startShot == null || endShot == null)
        {
            Debug.LogError(
                "[PlantCoreClearCutscene] AnimateCameraPath 중단 — " +
                "startShot 또는 endShot이 null입니다. Configure 메뉴를 실행하세요.", this);
            yield break;
        }

        Vector3 startPos = startShot.position;
        Vector3 endPos = endShot.position;
        // 이동 시작 시점의 XR Origin 회전을 유지 — 이동 중 강제 회전 없음
        Quaternion fixedRot = currentShotRotation;

        if (moveDuration <= 0f)
        {
            SetCurrentShotTransform(endPos, fixedRot);
            yield break;
        }

        bool useCurve = movementEaseCurve != null && movementEaseCurve.length >= 2;
        float elapsed = 0f;

        Debug.Log(
            $"[PlantCoreClearCutscene] 카메라 전진: {startPos} → {endPos} " +
            $"(거리 {Vector3.Distance(startPos, endPos):F2}m / {moveDuration}s)", this);

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float rawT = Mathf.Clamp01(elapsed / moveDuration);
            float easedT = useCurve
                ? movementEaseCurve.Evaluate(rawT)
                : Mathf.SmoothStep(0f, 1f, rawT);

            Vector3 pos = Vector3.Lerp(startPos, endPos, easedT);
            SetCurrentShotTransform(pos, fixedRot);
            yield return null;
        }

        SetCurrentShotTransform(endPos, fixedRot);
    }

    // 회전에서 Pitch/Roll 제거, Yaw만 유지 (VR 멀미 방지)
    private static Quaternion FlattenYaw(Quaternion rot)
    {
        Vector3 forward = Vector3.ProjectOnPlane(rot * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            return Quaternion.identity;
        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    // ── XR Origin 조작 ───────────────────────────────────────

    /// <summary>
    /// previewCamera와 XR Origin을 독립적으로 업데이트한다.
    /// previewMode 조건으로 분기하지 않는다 — 버그 방지.
    /// </summary>
    private void ApplyShotTransform()
    {
        // previewCamera는 항상 같이 이동 (에디터 Game 뷰 확인용)
        if (previewCamera != null)
        {
            if (!previewCamera.gameObject.activeSelf)
                previewCamera.gameObject.SetActive(true);
            previewCamera.transform.SetPositionAndRotation(
                currentShotPosition, currentShotRotation);
        }

        // XR Origin이 없으면 previewCamera만으로 종료
        if (xrOrigin == null || xrHead == null)
            return;

        // Yaw-only 회전 적용 — Pitch/Roll은 HMD에 맡긴다
        Vector3 forward = Vector3.ProjectOnPlane(
            currentShotRotation * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude > 0.001f)
            xrOrigin.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        // XR Origin을 xrHead가 목표 위치에 오도록 오프셋 조정
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
        if (xrOrigin == null || originalXRTransformSaved) return;
        originalXROriginPosition = xrOrigin.position;
        originalXROriginRotation = xrOrigin.rotation;
        originalXRTransformSaved = true;
    }

    private void RestoreOriginalXRTransform()
    {
        if (previewCamera != null)
            previewCamera.gameObject.SetActive(false);

        if (xrOrigin == null || !originalXRTransformSaved) return;
        xrOrigin.position = originalXROriginPosition;
        xrOrigin.rotation = originalXROriginRotation;
        originalXRTransformSaved = false;
    }

    // ── 페이드 ───────────────────────────────────────────────

    private IEnumerator FadeOutRoutine()
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeDuration));
        else if (previewFade != null)
            yield return PreviewFadeRoutine(1f);
    }

    private IEnumerator FadeInRoutine()
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeIn(fadeDuration));
        else if (previewFade != null)
            yield return PreviewFadeRoutine(0f);
    }

    // ── 환경 제어 ────────────────────────────────────────────

    private void SetRemoteAvatarsVisible(bool visible)
    {
        if (!hideRemoteAvatars) return;
        foreach (NetworkPlayer player in NetworkPlayer.All)
            if (player != null && !player.IsLocalNetworkRig)
                player.SetAvatarVisible(visible);
    }

    private void SetTutorialCanvasVisible(bool visible)
    {
        if (hideTutorialCanvas && hardwareRig != null)
            hardwareRig.SetTutorialCanvasVisible(visible);
    }

    // ── 씨앗 버튼 미리보기 트리거 ────────────────────────────

    private void TriggerSeedButton()
    {
        if (seedButton == null || _startSeqMethod == null)
        {
            Debug.LogWarning(
                "[PlantCoreClearCutscene] 씨앗 버튼을 트리거할 수 없습니다. " +
                "seedButton 또는 StartSequence 메서드를 찾지 못했습니다.", this);
            return;
        }
        // StartSequence()를 직접 호출하여 네트워크/역할 체크를 우회한다.
        // sequenceStarted 가드가 있으므로 중복 실행 없음.
        _startSeqMethod.Invoke(seedButton, null);
    }

    // ── 식물 상태 저장/복원 (미리보기) ──────────────────────

    private PlantPreviewState? CapturePlantState()
    {
        if (seedButton == null) return null;

        Transform seeds = _seedsField?.GetValue(seedButton) as Transform;
        GameObject sprouts = _sproutsField?.GetValue(seedButton) as GameObject;
        bool started = _seqStartedField != null
            && (bool)_seqStartedField.GetValue(seedButton);

        Vector3[] childScales = null;
        if (sprouts != null)
        {
            int count = sprouts.transform.childCount;
            childScales = new Vector3[count];
            for (int i = 0; i < count; i++)
                childScales[i] = sprouts.transform.GetChild(i).localScale;
        }

        return new PlantPreviewState
        {
            seedLocalPosition = seeds != null ? seeds.localPosition : Vector3.zero,
            sproutsWasActive = sprouts != null && sprouts.activeSelf,
            sproutChildScales = childScales,
            sequenceStarted = started
        };
    }

    private void RestorePlantState(PlantPreviewState state)
    {
        if (seedButton == null) return;

        // 파티클 즉시 제거
        plantParticle?.StopAll(clearParticles: true);

        // 재생 중인 효과음 중단 (반복 미리보기 시 중복 방지)
        if (_effectSource != null && _effectSource.isPlaying)
            _effectSource.Stop();
        AudioSource seedAudio = _seedDropAudioField?.GetValue(seedButton) as AudioSource;
        if (seedAudio != null && seedAudio.isPlaying)
            seedAudio.Stop();

        // 진행 중인 PlaySequence 코루틴 중단 후 초기 상태 복원
        seedButton.StopAllCoroutines();

        Transform seeds = _seedsField?.GetValue(seedButton) as Transform;
        GameObject sprouts = _sproutsField?.GetValue(seedButton) as GameObject;

        if (seeds != null) seeds.localPosition = state.seedLocalPosition;

        if (sprouts != null)
        {
            sprouts.SetActive(state.sproutsWasActive);
            if (state.sproutChildScales != null)
            {
                int count = Mathf.Min(
                    state.sproutChildScales.Length, sprouts.transform.childCount);
                for (int i = 0; i < count; i++)
                    sprouts.transform.GetChild(i).localScale = state.sproutChildScales[i];
            }
        }

        if (_seqStartedField != null)
            _seqStartedField.SetValue(seedButton, state.sequenceStarted);
    }

    // ── 미리보기 카메라/페이드 ───────────────────────────────

    private void SetupPreviewMode()
    {
        if (previewCamera == null)
        {
            var go = new GameObject("PlantCoreCutscenePreviewCamera");
            previewCamera = go.AddComponent<Camera>();
            previewCamera.depth = 100f;
            if (FindFirstObjectByType<AudioListener>() == null)
                go.AddComponent<AudioListener>();
            go.SetActive(false);
        }

        if (previewFade == null)
        {
            var canvasGO = new GameObject(
                "PlantCoreCutscenePreviewFade",
                typeof(Canvas), typeof(CanvasGroup));
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(canvasGO.transform, false);
            var rect = (RectTransform)black.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = black.GetComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;

            previewFade = canvasGO.GetComponent<CanvasGroup>();
            previewFade.alpha = 0f;
            previewFade.blocksRaycasts = false;
            previewFade.interactable = false;
        }

        previewMode = true;
        xrReady = true;
        Debug.LogWarning(
            "[PlantCoreClearCutscene] XR Origin 없음 — Game 뷰 미리보기로 재생합니다.", this);
    }

    private IEnumerator PreviewFadeRoutine(float targetAlpha)
    {
        if (previewFade == null) yield break;

        float startAlpha = previewFade.alpha;
        if (fadeDuration <= 0f)
        {
            previewFade.alpha = targetAlpha;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            previewFade.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
            yield return null;
        }
        previewFade.alpha = targetAlpha;
    }

    // ── ContextMenu 미리보기 ─────────────────────────────────

    [ContextMenu("Play Plant Core Clear Cutscene")]
    private void ContextPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[PlantCoreClearCutscene] Play 모드에서만 실행할 수 있습니다.", this);
            return;
        }

        if (isPlaying)
        {
            Debug.LogWarning(
                "[PlantCoreClearCutscene] 이미 재생 중입니다.", this);
            return;
        }

        // ContextMenu 미리보기: 항상 preview=true
        //   - 씨앗/새싹 애니메이션 트리거 + 종료 후 상태 복원
        //   - XR 없으면 추가로 previewCamera 모드 설정
        if (!xrReady)
            SetupPreviewMode();

        TryPlay(true);
    }

    // ── 정리 ─────────────────────────────────────────────────

    private void OnDisable()
    {
        StopAllCoroutines();

        if (!isPlaying) return;

        isPlaying = false;
        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        if (savedPlantState.HasValue)
        {
            RestorePlantState(savedPlantState.Value);
            savedPlantState = null;
        }
    }

    private void OnDestroy()
    {
        if (seedButton != null)
            seedButton.OnPressed -= OnMissionCleared;
        if (previewCamera != null)
            Destroy(previewCamera.gameObject);
    }

    // ── Gizmos ───────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (startShot == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(startShot.position, 0.15f);
        Gizmos.DrawRay(startShot.position, startShot.forward * 0.4f);

        if (endShot == null) return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(endShot.position, 0.15f);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(startShot.position, endShot.position);

        // 이동 방향 화살표 (중간 지점)
        Vector3 mid = Vector3.Lerp(startShot.position, endShot.position, 0.5f);
        Vector3 dir = (endShot.position - startShot.position);
        if (dir.sqrMagnitude > 0.0001f)
            Gizmos.DrawRay(mid, dir.normalized * 0.2f);

        // seedButton 위치 표시
        if (seedButton != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(seedButton.transform.position, 0.1f);
        }
    }
#endif
}
