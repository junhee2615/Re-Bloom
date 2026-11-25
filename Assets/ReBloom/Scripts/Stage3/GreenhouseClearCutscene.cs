using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mission 3-3 전기 코어 클리어 후 온실 복구 카메라 컷씬.
/// 전기 코어 → 물 상승/꽃 개화 → 중앙 나무 성장 → 온실 전체 풀백 순으로 순차 이동.
///
/// XR Origin 기반 위치 조정 — Main Camera 직접 이동 금지.
/// PlantCoreClearCutscene 구조를 참고한 직선 보간 방식 (Yaw-only 회전).
/// 카메라 미리보기 전용 — ElectricityCoreMission.Cleared 이벤트 연결 없음.
/// </summary>
[AddComponentMenu("ReBloom/Stage3/Greenhouse Clear Cutscene")]
public class GreenhouseClearCutscene : MonoBehaviour
{
    [Header("ElectricityShot — 전기 코어 정면")]
    [Tooltip("전기 코어 정면에서 바라보는 카메라 위치 (Head Position).")]
    [SerializeField] private Transform electricityShot;
    [Tooltip("ElectricityShot 도착 후 유지 시간(초).")]
    [SerializeField] private float electricityHoldDuration = 2f;

    [Header("WaterFlowerShot — 물 상승 + 꽃 개화")]
    [Tooltip("물 코어와 꽃 개화 위치가 보이는 카메라 위치 (Head Position).")]
    [SerializeField] private Transform waterFlowerShot;
    [Tooltip("ElectricityShot → WaterFlowerShot 이동 시간(초).")]
    [SerializeField] private float moveToWaterFlowerDuration = 3f;
    [Tooltip("WaterFlowerShot 도착 후 유지 시간(초).")]
    [SerializeField] private float waterFlowerHoldDuration = 2f;

    [Header("TreeShot — 나무 성장")]
    [Tooltip("중앙 EndingCore 나무 성장이 보이는 카메라 위치 (Head Position).")]
    [SerializeField] private Transform treeShot;
    [Tooltip("WaterFlowerShot → TreeShot 이동 시간(초).")]
    [SerializeField] private float moveToTreeDuration = 3f;
    [Tooltip("TreeShot 도착 후 유지 시간(초).")]
    [SerializeField] private float treeHoldDuration = 2f;

    [Header("FinalShot — 온실 전체 풀백")]
    [Tooltip("나무를 중심으로 뒤로 물러나며 온실 전체가 보이는 카메라 위치 (Head Position).")]
    [SerializeField] private Transform finalShot;
    [Tooltip("TreeShot → FinalShot 이동 시간(초). 느린 풀백 연출.")]
    [SerializeField] private float moveToFinalDuration = 5f;
    [Tooltip("FinalShot 도착 후 유지 시간(초).")]
    [SerializeField] private float finalHoldDuration = 2f;

    [Header("페이드")]
    [Tooltip("시작/종료 페이드 시간(초).")]
    [SerializeField] private float fadeDuration = 0.5f;

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

    private Vector3 originalXROriginPosition;
    private Quaternion originalXROriginRotation;
    private bool originalXRTransformSaved;

    private Vector3 currentShotPosition;
    private Quaternion currentShotRotation;
    private bool hasCurrentShot;

    private readonly ResonanceCutsceneOverride resonance = new ResonanceCutsceneOverride();

    // ── 미리보기 (XR 없는 에디터) ────────────────────────────
    private bool previewMode;
    private Camera previewCamera;
    private CanvasGroup previewFade;

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
                "[GreenhouseClearCutscene] XR Origin을 찾지 못했습니다. " +
                "Camera.main의 부모 계층에 'XR Origin'이 없습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        xrReady = true;

        Debug.Log(
            $"[GreenhouseClearCutscene] XR 초기화 완료\n" +
            $"  xrHead: {xrHead.name}\n" +
            $"  xrOrigin: {xrOrigin.name}\n" +
            $"  screenFade: {(screenFade != null ? screenFade.name : "없음")}\n" +
            $"  electricityShot: {(electricityShot != null ? electricityShot.name : "null ← Inspector 미연결!")}\n" +
            $"  waterFlowerShot: {(waterFlowerShot != null ? waterFlowerShot.name : "null ← Inspector 미연결!")}\n" +
            $"  treeShot: {(treeShot != null ? treeShot.name : "null ← Inspector 미연결!")}\n" +
            $"  finalShot: {(finalShot != null ? finalShot.name : "null ← Inspector 미연결!")}",
            this);
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
            return false;

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

        // 2. ElectricityShot으로 이동 (Fade 중이므로 보이지 않음)
        if (electricityShot != null)
        {
            SetCurrentShotTransform(electricityShot.position,
                FlattenYaw(electricityShot.rotation));
        }
        else
        {
            Debug.LogError(
                "[GreenhouseClearCutscene] electricityShot이 null — " +
                "ReBloom/Setup/Configure Greenhouse Clear Cutscene 메뉴를 실행하세요.", this);
        }

        // 3. FadeIn
        yield return FadeInRoutine();

        // 4. ElectricityShot Hold
        if (electricityHoldDuration > 0f)
            yield return new WaitForSeconds(electricityHoldDuration);

        // 5. WaterFlowerShot으로 이동
        if (waterFlowerShot != null)
            yield return AnimateShotMove(waterFlowerShot, moveToWaterFlowerDuration);

        // 6. WaterFlowerShot Hold
        if (waterFlowerHoldDuration > 0f)
            yield return new WaitForSeconds(waterFlowerHoldDuration);

        // 7. TreeShot으로 이동
        if (treeShot != null)
            yield return AnimateShotMove(treeShot, moveToTreeDuration);

        // 8. TreeShot Hold
        if (treeHoldDuration > 0f)
            yield return new WaitForSeconds(treeHoldDuration);

        // 9. FinalShot으로 이동 (느린 풀백)
        if (finalShot != null)
            yield return AnimateShotMove(finalShot, moveToFinalDuration);

        // 10. FinalShot Hold
        if (finalHoldDuration > 0f)
            yield return new WaitForSeconds(finalHoldDuration);

        // 11. FadeOut
        yield return FadeOutRoutine();

        // 12. 복원
        hasCurrentShot = false;
        RestoreOriginalXRTransform();
        resonance.Restore();
        SetRemoteAvatarsVisible(true);
        SetTutorialCanvasVisible(true);

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(false);

        yield return null;

        // 13. FadeIn
        yield return FadeInRoutine();

        isPlaying = false;
    }

    // ── 샷 간 직선 이동 (SmoothStep, Yaw-only 회전) ──────────

    private IEnumerator AnimateShotMove(Transform targetShot, float duration)
    {
        Vector3 startPos = currentShotPosition;
        Quaternion startRot = currentShotRotation;
        Vector3 endPos = targetShot.position;
        Quaternion endRot = FlattenYaw(targetShot.rotation);

        if (duration <= 0f)
        {
            SetCurrentShotTransform(endPos, endRot);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            SetCurrentShotTransform(
                Vector3.Lerp(startPos, endPos, t),
                Quaternion.Slerp(startRot, endRot, t));
            yield return null;
        }

        SetCurrentShotTransform(endPos, endRot);
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

    private void ApplyShotTransform()
    {
        if (previewCamera != null)
        {
            if (!previewCamera.gameObject.activeSelf)
                previewCamera.gameObject.SetActive(true);
            previewCamera.transform.SetPositionAndRotation(
                currentShotPosition, currentShotRotation);
        }

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

    // ── 미리보기 카메라/페이드 ───────────────────────────────

    private void SetupPreviewMode()
    {
        if (previewCamera == null)
        {
            var go = new GameObject("GreenhouseCutscenePreviewCamera");
            previewCamera = go.AddComponent<Camera>();
            previewCamera.depth = 100f;
            if (FindFirstObjectByType<AudioListener>() == null)
                go.AddComponent<AudioListener>();
            go.SetActive(false);
        }

        if (previewFade == null)
        {
            var canvasGO = new GameObject(
                "GreenhouseCutscenePreviewFade",
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
            "[GreenhouseClearCutscene] XR Origin 없음 — Game 뷰 미리보기로 재생합니다.", this);
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

    [ContextMenu("Play Greenhouse Clear Cutscene")]
    private void ContextPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning(
                "[GreenhouseClearCutscene] Play 모드에서만 실행할 수 있습니다.", this);
            return;
        }

        if (isPlaying)
        {
            Debug.LogWarning(
                "[GreenhouseClearCutscene] 이미 재생 중입니다.", this);
            return;
        }

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
    }

    private void OnDestroy()
    {
        if (previewCamera != null)
            Destroy(previewCamera.gameObject);
    }

    // ── Gizmos ───────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        DrawShotGizmo(electricityShot, Color.yellow, "E");
        DrawShotGizmo(waterFlowerShot, Color.cyan, "W");
        DrawShotGizmo(treeShot, Color.green, "T");
        DrawShotGizmo(finalShot, Color.red, "F");

        // 경로 선
        Transform[] shots = { electricityShot, waterFlowerShot, treeShot, finalShot };
        Gizmos.color = Color.white;
        for (int i = 0; i < shots.Length - 1; i++)
        {
            if (shots[i] != null && shots[i + 1] != null)
                Gizmos.DrawLine(shots[i].position, shots[i + 1].position);
        }
    }

    private static void DrawShotGizmo(Transform shot, Color color, string label)
    {
        if (shot == null) return;
        Gizmos.color = color;
        Gizmos.DrawWireSphere(shot.position, 0.2f);
        Gizmos.DrawRay(shot.position, shot.forward * 0.5f);
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { }
}
