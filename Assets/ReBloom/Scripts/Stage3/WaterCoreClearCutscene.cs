using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mission 3-1 물 코어 밸브 클리어 후 카메라 컷씬.
/// 카메라가 물 코어에서 배관을 따라 BabblingWater(물줄기) 방향으로 이동한다.
///
/// XR Origin 기반 위치 조정 — Main Camera 직접 이동 금지.
/// WaterfrontTeleporterCutscene 구조를 참고한 Bézier 곡선 + Arc-Length 보간 방식.
/// </summary>
[AddComponentMenu("ReBloom/Stage3/Water Core Clear Cutscene")]
public class WaterCoreClearCutscene : MonoBehaviour
{
    [Header("카메라 경로")]
    [Tooltip("물 코어와 밸브가 잘 보이는 시작 위치(Head Position).")]
    [SerializeField] private Transform startShot;
    [Tooltip("배관을 따라 중간 경유 지점. 선택 사항.")]
    [SerializeField] private Transform midWaypoint;
    [Tooltip("배관 끝과 중앙 나무가 잘 보이는 최종 카메라 위치.")]
    [SerializeField] private Transform endShot;

    [Header("타이밍")]
    [SerializeField] private float fadeDuration = 0.5f;
    [Tooltip("시작 위치에서 잠시 멈추는 시간(초).")]
    [SerializeField] private float startHoldDuration = 1f;
    [Tooltip("시작→종료 카메라 이동 시간(초).")]
    [SerializeField] private float moveDuration = 6f;
    [Tooltip("최종 위치 도착 후 유지 시간(초).")]
    [SerializeField] private float endHoldDuration = 3f;

    [Header("곡선 경로")]
    [Tooltip("웨이포인트 모서리 보간 거리(m). 0이면 직선.")]
    [SerializeField] private float cornerBlendDistance = 2f;
    [Tooltip("전체 이동 가속/감속 곡선. X=시간(0-1), Y=경로 진행(0-1).")]
    [SerializeField] private AnimationCurve movementEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("카메라 설정")]
    [SerializeField] private bool followYaw = true;
    [SerializeField] private bool followPitch;
    [Tooltip("이동 중 카메라 높이를 StartShot Y로 고정. VR 멀미 방지.")]
    [SerializeField] private bool lockCameraHeight = true;

    [Header("Yaw 회전 (VR 멀미 방지)")]
    [Tooltip("이동 초반 한 번만 회전하는 각도(도). 양수 = 왼쪽(반시계). 초기값 60°.")]
    [SerializeField] private float yawRotationDegrees = 60f;
    [Tooltip("Yaw 회전이 완료될 때까지의 시간(초). 이후에는 해당 방향 고정. 초기값 2s.")]
    [SerializeField] private float yawRotationDuration = 2f;

    [Header("연출 중 화면 정리")]
    [SerializeField] private bool disableResonance = true;
    [SerializeField] private bool disableBuiltinFog = true;
    [SerializeField] private bool hideRemoteAvatars = true;
    [SerializeField] private bool lockLocomotion = true;
    [SerializeField] private bool hideTutorialCanvas = true;

    [Header("물줄기 연출 (기존 BabblingWater / AudioSource)")]
    [Tooltip("BabblingWater_low 등 물줄기가 활성화될 오브젝트 목록. " +
             "카메라가 EndShot에 도달하는 순간 SetActive(true)된다.")]
    [SerializeField] private GameObject[] waterFlowObjects = Array.Empty<GameObject>();
    [Tooltip("물줄기 오브젝트 활성화 시점. 0 = 카메라 이동 시작, 1 = EndShot 도달.")]
    [Range(0f, 1f)]
    [SerializeField] private float waterActivationProgress = 1f;

    [Header("미션 연결")]
    [Tooltip("WaterValveRotate 참조. 비워두면 씬에서 자동 검색. " +
             "Start() 시 onValveCleared에 OnMissionCleared가 자동 등록된다.")]
    [SerializeField] private WaterValveRotate valveRotate;

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

    private bool waterActivated;

    private readonly ResonanceCutsceneOverride resonance =
        new ResonanceCutsceneOverride();

    // ── 미리보기 (XR 없는 에디터) ────────────────────────────
    private bool previewMode;
    private Camera previewCamera;
    private CanvasGroup previewFade;
    private bool[] previewWaterInitialStates;

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
                "[WaterCoreClearCutscene] XR Origin을 찾지 못했습니다. " +
                "Camera.main의 부모 계층에 'XR Origin'이 없습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        xrReady = true;

        // ── WaterValveRotate.onValveCleared 자동 연결 ────────
        if (valveRotate == null)
            valveRotate = FindFirstObjectByType<WaterValveRotate>(FindObjectsInactive.Include);

        if (valveRotate != null)
        {
            valveRotate.onValveCleared.RemoveListener(OnMissionCleared); // 중복 방지
            valveRotate.onValveCleared.AddListener(OnMissionCleared);
        }
        else
        {
            Debug.LogWarning(
                "[WaterCoreClearCutscene] WaterValveRotate를 찾지 못했습니다. " +
                "Inspector의 'Valve Rotate' 필드에 직접 연결하세요.", this);
        }

        Debug.Log(
            $"[WaterCoreClearCutscene] XR 초기화 완료\n" +
            $"  xrHead: {xrHead.name}\n" +
            $"  xrOrigin: {xrOrigin.name}\n" +
            $"  screenFade: {(screenFade != null ? screenFade.name : "없음")}\n" +
            $"  startShot: {(startShot != null ? startShot.name : "null ← Inspector 미연결!")}\n" +
            $"  midWaypoint: {(midWaypoint != null ? midWaypoint.name : "null")}\n" +
            $"  endShot: {(endShot != null ? endShot.name : "null ← Inspector 미연결!")}\n" +
            $"  waterFlowObjects: {waterFlowObjects.Length}개\n" +
            $"  valveRotate: {(valveRotate != null ? valveRotate.name + " (onValveCleared 연결됨)" : "null ← 미연결!")}",
            this);

        if (pendingPlay)
        {
            pendingPlay = false;
            TryPlay(false);
        }
    }

    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }

    // ── 외부 호출 API ────────────────────────────────────────

    public bool TryPlay()
    {
        return TryPlay(false);
    }

    private bool TryPlay(bool preview)
    {
        if (isPlaying)
            return false;

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
        waterActivated = false;
        previewWaterInitialStates = preview ? CaptureWaterInitialStates() : null;
        SaveOriginalXRTransform();

        Debug.Log(
            $"[WaterCoreClearCutscene] PlayRoutine 시작 (preview={preview})\n" +
            $"  xrOrigin: {(xrOrigin != null ? xrOrigin.name : "null")}\n" +
            $"  startShot: {(startShot != null ? $"{startShot.name} @ {startShot.position}" : "null ← 미연결!")}\n" +
            $"  endShot: {(endShot != null ? $"{endShot.name} @ {endShot.position}" : "null ← 미연결!")}",
            this);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);
        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        // 1. FadeOut
        yield return FadeOutRoutine();

        if (disableResonance || disableBuiltinFog)
            resonance.Disable(disableBuiltinFog);

        // 2. 시작 위치로 이동
        if (startShot != null)
        {
            Debug.Log(
                $"[WaterCoreClearCutscene] StartShot 이동: {startShot.position} " +
                $"(현재 xrOrigin: {xrOrigin?.position})", this);
            SetCurrentShotTransform(startShot.position, startShot.rotation);
        }
        else
        {
            Debug.LogError(
                "[WaterCoreClearCutscene] startShot이 null — Inspector에서 " +
                "WaterCoreClearStartShot Transform을 연결하세요.", this);
        }

        // 3. FadeIn
        yield return FadeInRoutine();

        // 4. 시작 위치 Hold
        if (startHoldDuration > 0f)
            yield return new WaitForSeconds(startHoldDuration);

        // 5. 카메라 이동 (Bézier 곡선)
        yield return AnimateCameraPath();

        // 6. 최종 위치 Hold
        if (endHoldDuration > 0f)
            yield return new WaitForSeconds(endHoldDuration);

        // 7. FadeOut
        yield return FadeOutRoutine();

        // 8. 복원
        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        yield return null;

        // 9. FadeIn
        yield return FadeInRoutine();

        isPlaying = false;

        if (preview)
            RestorePreviewWaterStates();
    }

    // ── 카메라 경로 보간 (Bézier + Arc-Length) ───────────────

    private IEnumerator AnimateCameraPath()
    {
        if (startShot == null || endShot == null)
        {
            Debug.LogError(
                $"[WaterCoreClearCutscene] AnimateCameraPath 중단 — " +
                $"startShot={startShot}, endShot={endShot}. " +
                "Inspector에서 Transform을 연결하거나 Configure 메뉴를 실행하세요.", this);
            yield break;
        }

        Debug.Log(
            $"[WaterCoreClearCutscene] 카메라 이동 시작: {startShot.position} → " +
            $"{(midWaypoint != null ? midWaypoint.position + " → " : "")}{endShot.position} " +
            $"({moveDuration}s)", this);

        Vector3 p0 = startShot.position;
        Vector3 p2 = endShot.position;

        bool hasMid = midWaypoint != null;
        Vector3 p1 = hasMid ? midWaypoint.position : Vector3.Lerp(p0, p2, 0.5f);

        // 카메라 높이 고정: StartShot Y를 기준으로 모든 웨이포인트 Y를 통일
        float fixedY = lockCameraHeight ? p0.y : float.NaN;
        if (lockCameraHeight)
        {
            p1.y = fixedY;
            p2.y = fixedY;
        }

        // ── 회전: 이동 초반(yawRotationDuration) 동안 한 번만 Yaw 회전, 이후 고정 ──
        // StartShot.rotation 그대로 사용 (수동 설정값 보존)
        // 양수 = 반시계(왼쪽), 음수 = 시계(오른쪽)
        Quaternion startYawQ = FlattenYaw(startShot.rotation);
        Quaternion finalYawQ = startYawQ * Quaternion.Euler(0f, -yawRotationDegrees, 0f);

        if (moveDuration <= 0f)
        {
            SetCurrentShotTransform(p2, finalYawQ);
            ActivateWaterFlow();
            yield break;
        }

        float seg1Len = Vector3.Distance(p0, p1);
        float seg2Len = Vector3.Distance(p1, p2);
        float blend = Mathf.Min(cornerBlendDistance, seg1Len * 0.5f, seg2Len * 0.5f);

        const int CurveSamples = 20;
        Vector3 b0, b2;
        float lenA, lenC;
        float[] cumDist;

        if (blend > 0.01f)
        {
            Vector3 dir01 = (p1 - p0).normalized;
            Vector3 dir12 = (p2 - p1).normalized;
            b0 = p1 - dir01 * blend;
            b2 = p1 + dir12 * blend;
            lenA = seg1Len - blend;
            lenC = seg2Len - blend;

            cumDist = new float[CurveSamples + 1];
            cumDist[0] = 0f;
            Vector3 prev = b0;
            for (int i = 1; i <= CurveSamples; i++)
            {
                float ct = (float)i / CurveSamples;
                Vector3 pt = QuadBezier(b0, p1, b2, ct);
                cumDist[i] = cumDist[i - 1] + Vector3.Distance(prev, pt);
                prev = pt;
            }
        }
        else
        {
            b0 = p1;
            b2 = p1;
            lenA = seg1Len;
            lenC = seg2Len;
            cumDist = new float[] { 0f, 0f };
        }

        float lenB = cumDist[cumDist.Length - 1];
        float totalLen = lenA + lenB + lenC;

        bool useCurve = movementEaseCurve != null && movementEaseCurve.length >= 2;
        float elapsed = 0f;

        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float rawT = Mathf.Clamp01(elapsed / moveDuration);

            float easedT = useCurve
                ? movementEaseCurve.Evaluate(rawT)
                : Mathf.SmoothStep(0f, 1f, rawT);

            // 물줄기 활성화 시점 체크
            if (!waterActivated && easedT >= waterActivationProgress)
                ActivateWaterFlow();

            float targetDist = Mathf.Clamp(easedT * totalLen, 0f, totalLen);
            Vector3 pos;

            if (targetDist <= lenA)
            {
                float segT = lenA > 0f ? targetDist / lenA : 1f;
                pos = Vector3.Lerp(p0, b0, segT);
            }
            else if (targetDist <= lenA + lenB)
            {
                float curveDist = targetDist - lenA;
                float curveT = ArcLengthToT(curveDist, cumDist);
                pos = QuadBezier(b0, p1, b2, curveT);
            }
            else
            {
                float segDist = targetDist - lenA - lenB;
                float segT = lenC > 0f ? segDist / lenC : 1f;
                pos = Vector3.Lerp(b2, p2, segT);
            }

            // ── 2-Phase Yaw 회전 ──────────────────────────────────
            // Phase 1 (0 ~ yawRotationDuration): 시작 Yaw → 목표 Yaw (SmoothStep)
            // Phase 2 (yawRotationDuration ~ moveDuration): finalYawQ 고정
            // MidWaypoint/EndShot 회전값은 사용하지 않는다.
            Quaternion rot;
            if (yawRotationDuration > 0f && elapsed <= yawRotationDuration)
            {
                float yawRaw = Mathf.Clamp01(elapsed / yawRotationDuration);
                float yawEased = Mathf.SmoothStep(0f, 1f, yawRaw);
                rot = Quaternion.Slerp(startYawQ, finalYawQ, yawEased);
            }
            else
            {
                rot = finalYawQ;
            }

            if (lockCameraHeight)
                pos.y = fixedY;

            SetCurrentShotTransform(pos, rot);
            yield return null;
        }

        Vector3 finalPos = p2;
        if (lockCameraHeight)
            finalPos.y = fixedY;
        SetCurrentShotTransform(finalPos, finalYawQ);

        if (!waterActivated)
            ActivateWaterFlow();
    }

    // 회전에서 Pitch/Roll 제거, Yaw만 유지 (VR 멀미 방지)
    private static Quaternion FlattenYaw(Quaternion rot)
    {
        Vector3 forward = Vector3.ProjectOnPlane(rot * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
            return Quaternion.identity;
        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

    private static Vector3 QuadBezier(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float u = 1f - t;
        return u * u * a + 2f * u * t * b + t * t * c;
    }

    private static float ArcLengthToT(float dist, float[] cumDist)
    {
        int n = cumDist.Length - 1;
        if (n <= 0 || dist <= 0f) return 0f;
        if (dist >= cumDist[n]) return 1f;

        for (int i = 1; i <= n; i++)
        {
            if (cumDist[i] >= dist)
            {
                float segLen = cumDist[i] - cumDist[i - 1];
                float frac = segLen > 0f ? (dist - cumDist[i - 1]) / segLen : 0f;
                return ((i - 1) + frac) / n;
            }
        }
        return 1f;
    }

    // ── 물줄기 활성화 ────────────────────────────────────────

    private void ActivateWaterFlow()
    {
        waterActivated = true;
        foreach (GameObject obj in waterFlowObjects)
        {
            if (obj != null)
                obj.SetActive(true);
        }
    }

    // ── XR Origin 조작 ───────────────────────────────────────

    private void ApplyShotTransform()
    {
        // previewCamera가 있으면 항상 같이 이동 (에디터 Game 뷰 확인용)
        if (previewCamera != null)
        {
            if (!previewCamera.gameObject.activeSelf)
                previewCamera.gameObject.SetActive(true);
            previewCamera.transform.SetPositionAndRotation(
                currentShotPosition, currentShotRotation);
        }

        // XR Origin이 없으면 (에디터 XR-less 환경) previewCamera만으로 종료
        if (xrOrigin == null || xrHead == null)
            return;

        if (followPitch)
        {
            xrOrigin.rotation = currentShotRotation;
        }
        else if (followYaw)
        {
            Vector3 forward = Vector3.ProjectOnPlane(
                currentShotRotation * Vector3.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
                xrOrigin.rotation =
                    Quaternion.LookRotation(forward.normalized, Vector3.up);
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
        if (previewCamera != null)
            previewCamera.gameObject.SetActive(false);

        if (xrOrigin == null || !originalXRTransformSaved)
            return;
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

    // ── 물줄기 상태 저장/복원 (미리보기) ────────────────────

    private bool[] CaptureWaterInitialStates()
    {
        if (waterFlowObjects == null)
            return Array.Empty<bool>();

        bool[] states = new bool[waterFlowObjects.Length];
        for (int i = 0; i < waterFlowObjects.Length; i++)
        {
            if (waterFlowObjects[i] != null)
                states[i] = waterFlowObjects[i].activeSelf;
        }
        return states;
    }

    private void RestorePreviewWaterStates()
    {
        if (previewWaterInitialStates == null || waterFlowObjects == null)
            return;

        int count = Mathf.Min(
            previewWaterInitialStates.Length, waterFlowObjects.Length);
        for (int i = 0; i < count; i++)
        {
            if (waterFlowObjects[i] != null)
                waterFlowObjects[i].SetActive(previewWaterInitialStates[i]);
        }
        previewWaterInitialStates = null;
    }

    // ── 미리보기 카메라/페이드 ───────────────────────────────

    private void SetupPreviewMode()
    {
        if (previewCamera == null)
        {
            var go = new GameObject("WaterCoreCutscenePreviewCamera");
            previewCamera = go.AddComponent<Camera>();
            previewCamera.depth = 100f;
            if (FindFirstObjectByType<AudioListener>() == null)
                go.AddComponent<AudioListener>();
            go.SetActive(false);
        }

        if (previewFade == null)
        {
            var canvasGO = new GameObject(
                "WaterCoreCutscenePreviewFade",
                typeof(Canvas), typeof(CanvasGroup));
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            var black = new GameObject("Black",
                typeof(RectTransform), typeof(Image));
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
            "[WaterCoreClearCutscene] XR Origin 없음 — Game 뷰 미리보기로 재생합니다.",
            this);
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
            previewFade.alpha =
                Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
            yield return null;
        }
        previewFade.alpha = targetAlpha;
    }

    // ── 미션 완료 연결 ───────────────────────────────────────

    /// <summary>
    /// WaterValveRotate.onValveCleared에 자동 등록됨.
    /// 멀티플레이: Render()에서 ChangeDetector가 모든 클라이언트에서 발화 → 각자 로컬 컷씬 재생.
    /// 미션 완료 후 물줄기 상태는 복원하지 않고 영구 활성화 상태로 유지.
    /// Debug Clear Mission 3-1도 onValveCleared.Invoke()를 통해 이 메서드를 호출한다.
    /// </summary>
    public void OnMissionCleared()
    {
        if (isPlaying)
            return;

        // onValveCleared의 Inspector 리스너(BabblingWater.SetActive(true))가 먼저
        // 실행됐을 수 있으므로, 컷씬 EndShot까지 물줄기를 다시 비활성화한다.
        // ActivateWaterFlow()가 EndShot 도달 시 최종 활성화한다.
        foreach (GameObject obj in waterFlowObjects)
        {
            if (obj != null && obj.activeSelf)
                obj.SetActive(false);
        }

        // preview=false → PlayRoutine에서 previewWaterInitialStates를 저장하지 않으므로
        // 종료 후 RestorePreviewWaterStates()가 호출돼도 물줄기 상태를 되돌리지 않는다.
        TryPlay(false);
    }

    // ── ContextMenu 미리보기 ─────────────────────────────────

    [ContextMenu("Play Water Core Clear Cutscene")]
    private void ContextPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[WaterCoreClearCutscene] Play 모드에서만 실행할 수 있습니다.", this);
            return;
        }

        if (isPlaying)
        {
            Debug.LogWarning(
                "[WaterCoreClearCutscene] 이미 재생 중입니다.", this);
            return;
        }

        // XR이 없는 에디터 테스트 환경에서만 previewCamera 모드 사용
        // XR Origin이 있으면 실제 모드(preview=false)로 실행
        bool usePreview = !xrReady;
        if (!xrReady)
            SetupPreviewMode();

        TryPlay(usePreview);
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

        RestorePreviewWaterStates();
    }

    private void OnDestroy()
    {
        if (previewCamera != null)
            Destroy(previewCamera.gameObject);

        if (valveRotate != null)
            valveRotate.onValveCleared.RemoveListener(OnMissionCleared);
    }

    // ── Gizmos ───────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (startShot == null || endShot == null) return;

        Vector3 p0 = startShot.position;
        Vector3 p2 = endShot.position;
        Vector3 p1 = midWaypoint != null
            ? midWaypoint.position
            : Vector3.Lerp(p0, p2, 0.5f);

        float seg1Len = Vector3.Distance(p0, p1);
        float seg2Len = Vector3.Distance(p1, p2);
        float blend = Mathf.Min(
            cornerBlendDistance, seg1Len * 0.5f, seg2Len * 0.5f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(p0, 0.3f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p1, 0.3f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(p2, 0.3f);

        if (blend > 0.01f)
        {
            Vector3 dir01 = (p1 - p0).normalized;
            Vector3 dir12 = (p2 - p1).normalized;
            Vector3 b0 = p1 - dir01 * blend;
            Vector3 b2 = p1 + dir12 * blend;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p0, b0);

            Gizmos.color = Color.yellow;
            Vector3 prev = b0;
            for (int i = 1; i <= 20; i++)
            {
                float t = (float)i / 20;
                float u = 1f - t;
                Vector3 pt = u * u * b0 + 2f * u * t * p1 + t * t * b2;
                Gizmos.DrawLine(prev, pt);
                prev = pt;
            }

            Gizmos.color = Color.green;
            Gizmos.DrawLine(b2, p2);
        }
        else
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p0, p1);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(p1, p2);
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { }
}
