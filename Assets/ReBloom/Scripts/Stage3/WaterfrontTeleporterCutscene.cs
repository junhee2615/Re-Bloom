using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Waterfront 구역 수변 복원 미션 클리어 후 WaterfrontToFinal 텔레포터를 안내하는 로컬 컷씬.
/// 카메라가 수로 쪽 낮은 지대에서 계단 위쪽까지 상승한 뒤 텔레포터를 향해 전진한다.
/// Stage3TeleporterCutscene(Mission 1)과 완전히 독립적으로 동작한다.
/// </summary>
[AddComponentMenu("ReBloom/Waterfront Teleporter Cutscene")]
public class WaterfrontTeleporterCutscene : MonoBehaviour
{
    [Header("카메라 경로")]
    [Tooltip("수로 쪽 낮은 지대의 시작 카메라 위치(Head Position).")]
    [SerializeField] private Transform startShot;
    [Tooltip("계단 위쪽 지대의 상승 완료 위치.")]
    [SerializeField] private Transform risePoint;
    [Tooltip("텔레포터가 잘 보이는 최종 카메라 위치.")]
    [SerializeField] private Transform endShot;

    [Header("타이밍")]
    [SerializeField] private float fadeDuration = 0.6f;
    [Tooltip("시작 → 상승 완료 구간 이동 시간(초).")]
    [SerializeField] private float riseDuration = 2.5f;
    [Tooltip("상승 완료 → 최종 위치 수평 이동 시간(초).")]
    [SerializeField] private float forwardDuration = 4f;
    [Tooltip("최종 위치에서 텔레포터 활성화를 보여주는 시간(초).")]
    [SerializeField] private float holdDuration = 2.5f;

    [Header("곡선 경로")]
    [Tooltip("상승→수평 전환 구간의 곡선 크기(m). 0이면 직선 경로.")]
    [SerializeField] private float cornerBlendDistance = 3f;
    [Tooltip("전체 이동의 가속/감속 곡선. X=시간(0-1), Y=경로 진행(0-1).")]
    [SerializeField] private AnimationCurve movementEaseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("텔레포터 VFX")]
    [SerializeField] private GameObject[] teleporterVfxObjects = Array.Empty<GameObject>();

    [Header("텔레포터 효과음")]
    [SerializeField] private AudioClip teleporterActivateClip;
    [Range(0f, 1f)]
    [SerializeField] private float teleporterActivateVolume = 0.4f;
    [SerializeField] private AudioSource effectSource;

    [Header("카메라 설정")]
    [SerializeField] private bool smoothCameraMovement = true;
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
    [SerializeField] private bool debugLog;

    public static event Action CutsceneFinished;

    private Camera runtimeCamera;
    private Transform xrHead;
    private Transform xrOrigin;
    private ScreenFade screenFade;
    private HardwareRig hardwareRig;
    private bool pendingPlay;
    private bool previewPlayback;
    private bool[] previewVfxInitialStates;

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;

    private Vector3 currentShotPosition;
    private Quaternion currentShotRotation;
    private bool hasCurrentShot;

    private readonly ResonanceCutsceneOverride resonance =
        new ResonanceCutsceneOverride();

    public bool IsPlaying => isPlaying;
    public bool CanPlay => xrReady && !isPlaying;

    private void Log(string msg)
    {
        if (!debugLog) return;
        Debug.Log(
            $"[Stage3Cutscene] F{Time.frameCount} T{Time.realtimeSinceStartup:F3} " +
            $"WaterfrontTeleporterCutscene - {msg}", this);
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
            Debug.LogError(
                "[WaterfrontTeleporterCutscene] XR Origin을 찾지 못했습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        ResolveTeleporterVfxObjects();
        xrReady = true;

        if (pendingPlay)
        {
            pendingPlay = false;
            TryPlay(previewPlayback);
        }
    }

    private void LateUpdate()
    {
        if (isPlaying && hasCurrentShot)
            ApplyShotTransform();
    }

    public bool TryPlay()
    {
        return TryPlay(false);
    }

    private bool TryPlay(bool preview)
    {
        Log($"TryPlay: preview={preview}, isPlaying={isPlaying}, xrReady={xrReady}");

        if (isPlaying)
        {
            Log("TryPlay: 거부 — 이미 재생 중");
            return false;
        }

        if (!xrReady)
        {
            previewPlayback = preview;
            pendingPlay = true;
            Log("TryPlay: xrReady=false → pendingPlay=true");
            return false;
        }

        previewPlayback = preview;
        Log("TryPlay: → PlayRoutine 시작");
        StartCoroutine(PlayRoutine(preview));
        return true;
    }

    [ContextMenu("Play Waterfront Teleporter Cutscene")]
    private void ContextPlayCutscene()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[WaterfrontTeleporterCutscene] Play 모드에서만 실행할 수 있습니다.");
            return;
        }

        TryPlay(true);
    }

    // ── 메인 연출 흐름 ───────────────────────────────────────

    private IEnumerator PlayRoutine(bool preview)
    {
        Log($"PlayRoutine 진입: preview={preview}");
        isPlaying = true;
        previewVfxInitialStates = preview ? CaptureVfxInitialStates() : null;
        SaveOriginalXRTransform();

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);

        SetRemoteAvatarsVisible(false);
        SetTutorialCanvasVisible(false);

        // 1. Fade Out
        yield return FadeOut();

        if (disableResonance || disableBuiltinFog)
            resonance.Disable(disableBuiltinFog);

        // 2. 시작 위치로 이동
        if (startShot != null)
            SetCurrentShotTransform(startShot.position, startShot.rotation);

        // 3. Fade In
        yield return FadeIn();

        // 4-6. 상승 → 전진, VFX 활성화
        yield return AnimateCameraPath();

        // 7. 텔레포터 Hold
        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        // 8. Fade Out
        yield return FadeOut();

        // 9. 원래 위치 복원
        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();

        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        yield return null;

        // 10. Fade In
        yield return FadeIn();

        isPlaying = false;
        Log("PlayRoutine 종료");

        if (preview)
            RestorePreviewVfxStates();

        previewPlayback = false;
        CutsceneFinished?.Invoke();
    }

    /// <summary>
    /// 수변 복원 컷씬이 FadeOut과 XR 복원을 끝낸 뒤 호출되는 연속 재생 루틴.
    /// 환경/아바타/이동 복원은 호출측 컷씬이 담당한다.
    /// </summary>
    internal IEnumerator PlayInSequenceRoutine()
    {
        Log($"PlayInSequenceRoutine 진입: isPlaying={isPlaying}");

        if (isPlaying)
        {
            Log("PlayInSequenceRoutine: 거부 — 이미 재생 중");
            yield break;
        }

        isPlaying = true;
        SaveOriginalXRTransform();

        if (startShot != null)
            SetCurrentShotTransform(startShot.position, startShot.rotation);

        yield return FadeIn();

        yield return AnimateCameraPath();

        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        yield return FadeOut();

        hasCurrentShot = false;
        RestoreOriginalXRTransform();

        yield return null;
        yield return FadeIn();

        isPlaying = false;
        Log("PlayInSequenceRoutine 종료");
        CutsceneFinished?.Invoke();
    }

    internal void ActivateForLateJoin()
    {
        Log("ActivateForLateJoin: 텔레포터 VFX 활성화 (사운드 없음)");
        ResolveTeleporterVfxObjects();
        foreach (GameObject vfx in teleporterVfxObjects)
        {
            if (vfx != null)
                vfx.SetActive(true);
        }
    }

    /// <summary>
    /// 디버그용: 컷씬 없이 텔레포터 VFX/사운드를 켜고 CutsceneFinished 를 보내
    /// requiredWaterfrontCutscene 으로 잠긴 ZoneTeleporter 를 바로 해제한다.
    /// 텔레포터 탑승 판정은 호스트가 하므로 호스트에서 실행해야 실제로 탑승이 열린다.
    /// </summary>
    public void DebugForceActivate()
    {
        if (isPlaying)
        {
            Debug.LogWarning(
                "[WaterfrontTeleporterCutscene] 컷씬 재생 중이라 강제 활성화를 건너뜁니다. " +
                "컷씬이 끝나면 자동으로 활성화됩니다.", this);
            return;
        }

        Log("DebugForceActivate: 컷씬 생략, 텔레포터 강제 활성화");
        ResolveTeleporterVfxObjects();
        ActivateTeleporter();
        CutsceneFinished?.Invoke();
    }


    // ── 카메라 경로 보간 (Rounded-Corner + Arc-Length) ────────

    private IEnumerator AnimateCameraPath()
    {
        if (startShot == null || endShot == null)
        {
            if (endShot != null)
                SetCurrentShotTransform(endShot.position, endShot.rotation);
            ActivateTeleporter();
            yield break;
        }

        Vector3 p0 = startShot.position;
        Quaternion r0 = startShot.rotation;
        Vector3 p2 = endShot.position;
        Quaternion r2 = endShot.rotation;

        bool hasRise = risePoint != null;
        Vector3 p1 = hasRise ? risePoint.position : Vector3.Lerp(p0, p2, 0.45f);
        Quaternion r1 = hasRise ? risePoint.rotation : Quaternion.Slerp(r0, r2, 0.45f);

        float totalDuration = riseDuration + forwardDuration;
        if (totalDuration <= 0f)
        {
            SetCurrentShotTransform(p2, r2);
            ActivateTeleporter();
            yield break;
        }

        float seg1Len = Vector3.Distance(p0, p1);
        float seg2Len = Vector3.Distance(p1, p2);
        float blend = Mathf.Min(
            cornerBlendDistance, seg1Len * 0.5f, seg2Len * 0.5f);

        const int CurveSamples = 20;
        Vector3 b0, b2;
        float lenA, lenC;
        float[] curveCumDist;

        if (blend > 0.01f)
        {
            Vector3 dir01 = (p1 - p0).normalized;
            Vector3 dir12 = (p2 - p1).normalized;
            b0 = p1 - dir01 * blend;
            b2 = p1 + dir12 * blend;
            lenA = seg1Len - blend;
            lenC = seg2Len - blend;

            curveCumDist = new float[CurveSamples + 1];
            curveCumDist[0] = 0f;
            Vector3 prev = b0;
            for (int i = 1; i <= CurveSamples; i++)
            {
                float ct = (float)i / CurveSamples;
                Vector3 pt = QuadBezier(b0, p1, b2, ct);
                curveCumDist[i] = curveCumDist[i - 1] + Vector3.Distance(prev, pt);
                prev = pt;
            }
        }
        else
        {
            b0 = p1;
            b2 = p1;
            lenA = seg1Len;
            lenC = seg2Len;
            curveCumDist = new float[] { 0f, 0f };
        }

        float lenB = curveCumDist[curveCumDist.Length - 1];
        float totalLen = lenA + lenB + lenC;
        float cornerMidFrac = totalLen > 0f
            ? (lenA + lenB * 0.5f) / totalLen
            : 0.5f;

        bool useCurve = movementEaseCurve != null && movementEaseCurve.length >= 2;
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;
            float rawT = Mathf.Clamp01(elapsed / totalDuration);

            float easedT;
            if (useCurve)
                easedT = movementEaseCurve.Evaluate(rawT);
            else if (smoothCameraMovement)
                easedT = Mathf.SmoothStep(0f, 1f, rawT);
            else
                easedT = rawT;

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
                float curveT = ArcLengthToT(curveDist, curveCumDist);
                pos = QuadBezier(b0, p1, b2, curveT);
            }
            else
            {
                float segDist = targetDist - lenA - lenB;
                float segT = lenC > 0f ? segDist / lenC : 1f;
                pos = Vector3.Lerp(b2, p2, segT);
            }

            float distFrac = totalLen > 0f ? targetDist / totalLen : 1f;
            Quaternion rot;
            if (distFrac <= cornerMidFrac)
            {
                float rf = cornerMidFrac > 0f ? distFrac / cornerMidFrac : 1f;
                rot = Quaternion.Slerp(r0, r1, rf);
            }
            else
            {
                float rf = (1f - cornerMidFrac) > 0f
                    ? (distFrac - cornerMidFrac) / (1f - cornerMidFrac)
                    : 1f;
                rot = Quaternion.Slerp(r1, r2, rf);
            }

            SetCurrentShotTransform(pos, rot);
            yield return null;
        }

        SetCurrentShotTransform(p2, r2);
        ActivateTeleporter();
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

    // ── 텔레포터 VFX / 사운드 ────────────────────────────────

    private void ActivateTeleporter()
    {
        Log("ActivateTeleporter");

        foreach (GameObject vfx in teleporterVfxObjects)
        {
            if (vfx != null)
                vfx.SetActive(true);
        }

        if (teleporterActivateClip == null)
            return;

        if (effectSource == null)
        {
            effectSource = gameObject.AddComponent<AudioSource>();
            effectSource.playOnAwake = false;
            effectSource.loop = false;
            effectSource.spatialBlend = 0f;
        }

        effectSource.Stop();
        effectSource.volume = teleporterActivateVolume;
        effectSource.PlayOneShot(teleporterActivateClip);
    }

    private void ResolveTeleporterVfxObjects()
    {
        if (teleporterVfxObjects != null && teleporterVfxObjects.Length > 0)
            return;

        Transform teleporter =
            FindTransformInScene("WaterfrontToFinalTeleporter");
        if (teleporter == null)
            return;

        GameObject fireCircle = FindChildGameObject(teleporter, "Fire Circle");
        GameObject splash = FindChildGameObject(teleporter, "Splash");

        if (fireCircle != null) fireCircle.SetActive(false);
        if (splash != null) splash.SetActive(false);

        teleporterVfxObjects = new[] { fireCircle, splash };
    }

    // ── XR Origin 조작 ───────────────────────────────────────

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
        if (xrOrigin == null || !originalXRTransformSaved)
            return;

        xrOrigin.position = originalXROriginPosition;
        xrOrigin.rotation = originalXROriginRotation;
        originalXRTransformSaved = false;
    }

    // ── 페이드 ───────────────────────────────────────────────

    private IEnumerator FadeOut()
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeDuration));
    }

    private IEnumerator FadeIn()
    {
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeIn(fadeDuration));
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

    // ── VFX 상태 저장/복원 (미리보기) ────────────────────────

    private bool[] CaptureVfxInitialStates()
    {
        if (teleporterVfxObjects == null)
            return Array.Empty<bool>();

        bool[] states = new bool[teleporterVfxObjects.Length];
        for (int i = 0; i < teleporterVfxObjects.Length; i++)
        {
            if (teleporterVfxObjects[i] != null)
                states[i] = teleporterVfxObjects[i].activeSelf;
        }
        return states;
    }

    private void RestorePreviewVfxStates()
    {
        if (previewVfxInitialStates == null || teleporterVfxObjects == null)
            return;

        int count = Mathf.Min(
            previewVfxInitialStates.Length,
            teleporterVfxObjects.Length);

        for (int i = 0; i < count; i++)
        {
            if (teleporterVfxObjects[i] != null)
                teleporterVfxObjects[i].SetActive(previewVfxInitialStates[i]);
        }

        previewVfxInitialStates = null;
    }

    // ── Gizmos ────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (startShot == null || endShot == null) return;

        Vector3 p0 = startShot.position;
        Vector3 pEnd = endShot.position;
        Vector3 p1 = risePoint != null
            ? risePoint.position
            : Vector3.Lerp(p0, pEnd, 0.45f);

        float seg1Len = Vector3.Distance(p0, p1);
        float seg2Len = Vector3.Distance(p1, pEnd);
        float blend = Mathf.Min(cornerBlendDistance, seg1Len * 0.5f, seg2Len * 0.5f);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(p0, 0.3f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(p1, 0.3f);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(pEnd, 0.3f);

        if (blend > 0.01f)
        {
            Vector3 dir01 = (p1 - p0).normalized;
            Vector3 dir12 = (pEnd - p1).normalized;
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
            Gizmos.DrawLine(b2, pEnd);

            Gizmos.color = new Color(1f, 0.5f, 0f);
            Gizmos.DrawWireSphere(b0, 0.15f);
            Gizmos.DrawWireSphere(b2, 0.15f);
        }
        else
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p0, p1);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(p1, pEnd);
        }
    }
#endif

    // ── 정리 ─────────────────────────────────────────────────

    private void OnDisable()
    {
        StopAllCoroutines();
        CleanupAfterCutscene();
    }

    private void OnDestroy()
    {
        if (effectSource != null)
            effectSource.Stop();
    }

    private void CleanupAfterCutscene()
    {
        if (!isPlaying)
        {
            if (effectSource != null)
                effectSource.Stop();

            if (previewPlayback)
                RestorePreviewVfxStates();

            previewPlayback = false;
            return;
        }

        isPlaying = false;
        hasCurrentShot = false;

        if (effectSource != null)
            effectSource.Stop();

        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        if (previewPlayback)
            RestorePreviewVfxStates();

        previewPlayback = false;
    }

    // ── 유틸리티 ─────────────────────────────────────────────

    private static Transform FindTransformInScene(string targetName)
    {
        Transform[] transforms =
            FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (Transform t in transforms)
        {
            if (t.name == targetName)
                return t;
        }
        return null;
    }

    private static GameObject FindChildGameObject(
        Transform parent, string targetName)
    {
        foreach (Transform child in
                 parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == targetName)
                return child.gameObject;
        }
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CutsceneFinished = null;
    }
}
