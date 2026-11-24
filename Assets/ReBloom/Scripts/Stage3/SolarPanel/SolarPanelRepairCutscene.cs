using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 고장 패널이 수리될 때 짧은 컷씬을 재생한다.
    ///
    /// 페이드 아웃 → XR Origin을 패널 촬영 위치로 이동 → 페이드 인 → 복구 외형 전환 →
    /// 잠시 보여줌 → 페이드 아웃 → XR Origin 복귀 → 페이드 인.
    ///
    /// 기존 <see cref="Stage3RiverCutscene"/>, <see cref="Mission1ClearCutscene"/>과 같은 패턴이다.
    /// 각 플레이어가 로컬에서 독립 재생한다(네트워크 전송 없음).
    ///
    /// 배치: Stage 3 씬에 하나. Inspector에서 패널별 촬영 위치를 연결한다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Panel Repair Cutscene")]
    public class SolarPanelRepairCutscene : MonoBehaviour
    {
        [Serializable]
        public class PanelEntry
        {
            [Tooltip("수리를 감지할 패널의 SolarReflector.")]
            public SolarReflector reflector;

            [Tooltip("같은 패널의 SolarPanelCleanView. 외형 갱신을 보류하는 데 쓴다.")]
            public SolarPanelCleanView cleanView;

            [Tooltip("컷씬 카메라 위치·방향. 빈 GameObject를 패널 앞에 배치한다.")]
            public Transform shotPoint;
        }

        [Header("패널")]
        [SerializeField] private PanelEntry[] panels = Array.Empty<PanelEntry>();

        [Header("페이드")]
        [SerializeField] private float fadeDuration = 0.5f;

        [Header("촬영")]
        [Tooltip("복구된 패널을 보여주는 시간(초). 발광 미사용 시 페이드 사이의 순수 대기.")]
        [SerializeField] private float holdDuration = 1.5f;

        [Header("발광")]
        [SerializeField] private bool useGlow = true;

        [Tooltip("고장 패널을 보여준 뒤 발광까지의 대기(초).")]
        [SerializeField] private float preGlowDelay = 0.6f;

        [Tooltip("발광의 전체 지속 시간(초). 절반 지점에서 외형이 바뀐다.")]
        [SerializeField] private float glowDuration = 1.8f;

        [Tooltip("발광 완료 뒤 깨끗한 패널을 보여주는 시간(초).")]
        [SerializeField] private float postGlowHold = 1.2f;

        [Tooltip("테두리 발광 색상(HDR). Bloom threshold(0.9)를 넘는 값이면 블룸 헤일로가 생긴다.")]
        [ColorUsage(false, true)]
        [SerializeField] private Color glowColor = new Color(0f, 2f, 1.6f, 1f);

        [Header("효과음")]
        [Tooltip("발광 최고조 시점에 재생할 효과음(alive_decal 등).")]
        [SerializeField] private AudioClip repairClip;

        [Tooltip("효과음 볼륨.")]
        [Range(0f, 1f)]
        [SerializeField] private float repairVolume = 0.7f;

        [Header("복구 완료 아이콘")]
        [Tooltip("복구 완료 시 표시할 스프라이트(체크 아이콘). 비어 있으면 아이콘을 표시하지 않는다.")]
        [SerializeField] private Sprite repairMarkerSprite;

        [Tooltip("아이콘의 월드 크기(미터).")]
        [SerializeField] private float repairMarkerSize = 0.35f;

        [Tooltip("패널 중심에서 위쪽으로 띄울 높이(미터).")]
        [SerializeField] private float repairMarkerHeight = 0.5f;

        [Tooltip("아이콘이 보이는 시간(초). 이후 사라지는 애니메이션이 재생된다.")]
        [SerializeField] private float repairMarkerShowDuration = 1.0f;

        [Tooltip("월드 Y축 기준으로 아이콘을 위아래로 추가 이동(미터).")]
        [SerializeField] private float repairMarkerVerticalOffset;

        [Tooltip("패널에서 컷씬 카메라 방향으로 아이콘을 이동(미터). 양수면 카메라에 가까워진다.")]
        [SerializeField] private float repairMarkerTowardCameraOffset;

        [Header("카메라")]
        [SerializeField] private bool followYaw = true;
        [Tooltip("VR 멀미를 줄이려면 꺼 둔다.")]
        [SerializeField] private bool followPitch;

        [Header("연출 중 화면 정리")]
        [SerializeField] private bool hideRemoteAvatars = true;
        [SerializeField] private bool lockLocomotion = true;
        [SerializeField] private bool hideTutorialCanvas = true;

        [Header("Debug")]
        [SerializeField] private bool xrReady;
        [SerializeField] private bool isPlaying;

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

        private readonly ResonanceCutsceneOverride resonance =
            new ResonanceCutsceneOverride();

        private SolarPanelRepairGlow activeGlow;
        private AudioSource repairAudioSource;
        private RootFoundMarker activeMarker;

        // 컷씬 중 들어온 추가 수리 이벤트를 순서대로 처리한다.
        private readonly Queue<PanelEntry> pendingQueue = new Queue<PanelEntry>();

        // 현재 외형을 보류 중인 엔트리. 중단 시 풀어야 한다.
        private PanelEntry activePanelEntry;

        public bool IsPlaying => isPlaying;

        /// <summary>컷씬 재생 중이거나 대기 큐에 패널이 남아 있는가. 전체 클리어 컷씬이 대기할 때 쓴다.</summary>
        public bool IsBusy => isPlaying || pendingQueue.Count > 0;

        // ============================================================
        // Initialize
        // ============================================================

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
                    "[SolarPanelRepairCutscene] XR Origin을 찾지 못했습니다.", this);
                yield break;
            }

            screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
            hardwareRig = FindFirstObjectByType<HardwareRig>();
            xrReady = true;
        }

        private void OnEnable()
        {
            foreach (PanelEntry entry in panels)
            {
                if (entry?.reflector != null)
                    entry.reflector.Repaired += OnPanelRepaired;
            }
        }

        private void OnDisable()
        {
            foreach (PanelEntry entry in panels)
            {
                if (entry?.reflector != null)
                    entry.reflector.Repaired -= OnPanelRepaired;
            }

            StopAllCoroutines();

            if (!isPlaying) return;

            ForceCleanup();
        }

        private void LateUpdate()
        {
            if (isPlaying && hasCurrentShot)
                ApplyShotTransform();
        }

        // ============================================================
        // Event
        // ============================================================

        private void OnPanelRepaired(SolarReflector reflector)
        {
            PanelEntry entry = FindEntry(reflector);
            if (entry == null) return;

            if (entry.shotPoint == null)
            {
                Debug.LogWarning(
                    $"[SolarPanelRepairCutscene] {reflector.name}에 촬영 위치가 없어 컷씬을 건너뜁니다.",
                    this);
                return;
            }

            if (!xrReady)
            {
                Debug.LogWarning(
                    $"[SolarPanelRepairCutscene] XR이 아직 준비되지 않아 {reflector.name} 컷씬을 건너뜁니다.",
                    this);
                return;
            }

            if (isPlaying)
            {
                // 외형만 먼저 보류하고 큐에 넣는다.
                if (entry.cleanView != null)
                    entry.cleanView.SetHoldVisual(true);

                pendingQueue.Enqueue(entry);
                return;
            }

            StartCutscene(entry);
        }

        private PanelEntry FindEntry(SolarReflector reflector)
        {
            foreach (PanelEntry entry in panels)
            {
                if (entry?.reflector == reflector)
                    return entry;
            }
            return null;
        }

        // ============================================================
        // Cutscene
        // ============================================================

        private void StartCutscene(PanelEntry entry)
        {
            if (entry.cleanView != null)
                entry.cleanView.SetHoldVisual(true);

            activePanelEntry = entry;
            isPlaying = true;
            StartCoroutine(PlayRoutine(entry));
        }

        private IEnumerator PlayRoutine(PanelEntry entry)
        {
            // 1. 이동 잠금, UI 숨김
            if (lockLocomotion && hardwareRig != null)
                hardwareRig.SetLocomotionLocked(true);
            SetRemoteAvatarsVisible(false);
            SetTutorialCanvasVisible(false);

            // 2. 페이드 아웃
            yield return FadeOutRoutine();

            // 3. 검은 화면에서 Resonance 끄기, XR Origin 이동
            resonance.Disable(true);
            SaveOriginalXRTransform();

            SetCurrentShotTransform(
                entry.shotPoint.position,
                entry.shotPoint.rotation);

            // 4. 페이드 인
            yield return FadeInRoutine();

            // 5-6. 발광 연출 → 외형 전환 → 여운
            if (useGlow && entry.cleanView != null)
            {
                if (preGlowDelay > 0f)
                    yield return new WaitForSeconds(preGlowDelay);

                yield return GlowRoutine(entry);

                if (postGlowHold > 0f)
                    yield return new WaitForSeconds(postGlowHold);
            }
            else
            {
                if (entry.cleanView != null)
                    entry.cleanView.SetHoldVisual(false);

                activePanelEntry = null;

                if (holdDuration > 0f)
                    yield return new WaitForSeconds(holdDuration);
            }

            // 7. 페이드 아웃
            yield return FadeOutRoutine();

            // 8. XR Origin 복귀, Resonance 복구
            hasCurrentShot = false;
            RestoreOriginalXRTransform();
            resonance.Restore();

            SetRemoteAvatarsVisible(true);
            SetTutorialCanvasVisible(true);

            if (lockLocomotion && hardwareRig != null)
                hardwareRig.SetLocomotionLocked(false);

            yield return null;

            // 9. 페이드 인
            yield return FadeInRoutine();

            isPlaying = false;

            // 대기 중인 패널이 있으면 이어서 재생
            if (pendingQueue.Count > 0)
                StartCutscene(pendingQueue.Dequeue());
        }

        private IEnumerator GlowRoutine(PanelEntry entry)
        {
            SolarPanelRepairGlow glow = new SolarPanelRepairGlow();
            glow.Setup(entry.cleanView, glowColor);
            activeGlow = glow;

            float half = glowDuration * 0.5f;

            // 발광 세기 올림
            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                glow.SetIntensity(Mathf.SmoothStep(0f, 1f, elapsed / half));
                yield return null;
            }
            glow.SetIntensity(1f);

            // 최대 발광 시점에 외형 전환 + 효과음
            if (entry.cleanView != null)
                entry.cleanView.SetHoldVisual(false);
            activePanelEntry = null;

            PlayRepairSound();
            StartCoroutine(RepairMarkerRoutine(entry));

            // 발광 세기 내림
            elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                glow.SetIntensity(Mathf.SmoothStep(1f, 0f, elapsed / half));
                yield return null;
            }

            glow.Cleanup();
            activeGlow = null;
        }

        private void PlayRepairSound()
        {
            if (repairClip == null) return;

            if (repairAudioSource == null)
            {
                repairAudioSource = gameObject.AddComponent<AudioSource>();
                repairAudioSource.playOnAwake = false;
                repairAudioSource.spatialBlend = 0f;
            }

            repairAudioSource.PlayOneShot(repairClip, repairVolume);
        }

        // ============================================================
        // Repair Marker
        // ============================================================

        private IEnumerator RepairMarkerRoutine(PanelEntry entry)
        {
            if (repairMarkerSprite == null) yield break;

            Transform panel = entry.reflector.transform;
            Vector3 markerPos = panel.position + Vector3.up * repairMarkerHeight;
            markerPos += Vector3.up * repairMarkerVerticalOffset;

            if (runtimeCamera != null && repairMarkerTowardCameraOffset != 0f)
            {
                Vector3 toCamera = (runtimeCamera.transform.position - panel.position).normalized;
                markerPos += toCamera * repairMarkerTowardCameraOffset;
            }

            GameObject go = new GameObject("_RepairMarker");
            RootFoundMarker marker = go.AddComponent<RootFoundMarker>();
            marker.Setup(repairMarkerSprite, repairMarkerSize, markerPos);
            go.SetActive(false);

            activeMarker = marker;
            marker.Show();

            yield return new WaitForSeconds(repairMarkerShowDuration);

            marker.Hide();
            yield return new WaitForSeconds(0.3f);

            if (marker != null && marker.gameObject != null)
                Destroy(marker.gameObject);

            if (activeMarker == marker)
                activeMarker = null;
        }

        // ============================================================
        // XR Shot
        // ============================================================

        private void ApplyShotTransform()
        {
            if (xrOrigin == null || xrHead == null) return;

            if (followPitch)
            {
                xrOrigin.rotation = currentShotRotation;
            }
            else if (followYaw)
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    currentShotRotation * Vector3.forward, Vector3.up);
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
            if (xrOrigin == null || originalXRTransformSaved) return;

            originalXROriginPosition = xrOrigin.position;
            originalXROriginRotation = xrOrigin.rotation;
            originalXRTransformSaved = true;
        }

        private void RestoreOriginalXRTransform()
        {
            if (xrOrigin == null || !originalXRTransformSaved) return;

            xrOrigin.position = originalXROriginPosition;
            xrOrigin.rotation = originalXROriginRotation;
            originalXRTransformSaved = false;
        }

        // ============================================================
        // Fade
        // ============================================================

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

        // ============================================================
        // Avatar / UI
        // ============================================================

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

        // ============================================================
        // Safety Restore
        // ============================================================

        private void ForceCleanup()
        {
            isPlaying = false;
            hasCurrentShot = false;

            activeGlow?.Cleanup();
            activeGlow = null;

            if (activeMarker != null)
            {
                Destroy(activeMarker.gameObject);
                activeMarker = null;
            }

            // 보류 중인 외형을 모두 풀어준다.
            if (activePanelEntry?.cleanView != null)
                activePanelEntry.cleanView.SetHoldVisual(false);

            activePanelEntry = null;

            while (pendingQueue.Count > 0)
            {
                PanelEntry queued = pendingQueue.Dequeue();
                if (queued?.cleanView != null)
                    queued.cleanView.SetHoldVisual(false);
            }

            RestoreOriginalXRTransform();
            resonance.Restore();

            SetRemoteAvatarsVisible(true);
            SetTutorialCanvasVisible(true);

            if (lockLocomotion && hardwareRig != null)
                hardwareRig.SetLocomotionLocked(false);
        }
    }
}
