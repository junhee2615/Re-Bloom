using System;
using System.Collections;
using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 태양광 패널 4개가 모두 복구되면 재생하는 전체 클리어 컷씬.
    ///
    /// <see cref="SolarMissionManager.Cleared"/>를 트리거로 받아 개별 복구 컷씬이
    /// 모두 끝난 뒤 시작한다. 연출 순서:
    ///
    ///  01. 페이드 아웃 → ShotPoint로 XR Origin 이동 → 페이드 인 (전체 시설 조망)
    ///  02. 네 패널의 아웃라인 발광 — 깜빡이다 안정 (SolarPanelRepairGlow 재사용)
    ///  03. 빛 입자 활성화 (SolarFacilityClearParticle.Play)
    ///  04. 페이드 아웃 → XR Origin 복원 → 페이드 인
    ///
    /// 발광과 파티클은 컷씬 종료 후에도 영구 유지한다(Cleanup/Stop하지 않는다).
    /// 늦게 접속한 플레이어에게는 컷씬 없이 최종 상태만 적용한다.
    ///
    /// 배치: Stage 3 씬에 하나. Editor 메뉴로 참조를 자동 연결한다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Facility Clear Cutscene")]
    public class SolarFacilityClearCutscene : MonoBehaviour
    {
        [Serializable]
        public class PanelEntry
        {
            public SolarReflector reflector;
            public SolarPanelCleanView cleanView;
            public SolarFacilityClearParticle particle;
        }

        [Header("패널")]
        [SerializeField] PanelEntry[] panels = Array.Empty<PanelEntry>();

        [Header("ShotPoint")]
        [Tooltip("전체 시설 조망 카메라 위치. SolarFacilityClearShotPoint.")]
        [SerializeField] Transform shotPoint;

        [Header("개별 복구 컷씬 참조")]
        [Tooltip("개별 패널 복구 컷씬. IsBusy가 false가 될 때까지 기다린다.")]
        [SerializeField] SolarPanelRepairCutscene repairCutscene;

        [Header("텔레포터 안내 컷씬")]
        [Tooltip("시설 컷씬 종료 후 연속 재생할 텔레포터 안내 컷씬. 미리보기에서는 사용하지 않는다.")]
        [SerializeField] Stage3TeleporterCutscene teleporterCutscene;

        [Header("페이드")]
        [SerializeField] float fadeDuration = 0.5f;

        [Header("01. 전체 시설 조망")]
        [Tooltip("ShotPoint에서 시설을 보여주는 시간(초).")]
        [SerializeField] float viewHold = 1.5f;

        [Header("02. 아웃라인 발광")]
        [Tooltip("발광에 쓰는 HDR 색상. 개별 복구 컷씬과 동일하게 설정한다.")]
        [ColorUsage(false, true)]
        [SerializeField] Color glowColor = new Color(0f, 2f, 1.6f, 1f);

        [Tooltip("깜빡임 총 지속 시간(초).")]
        [SerializeField] float blinkDuration = 2.0f;

        [Tooltip("깜빡임 횟수.")]
        [SerializeField] int blinkCount = 3;

        [Tooltip("아웃라인 점멸이 시작되기 전 대기 시간(초).")]
        [Min(0f)]
        [SerializeField] float blinkStartDelay = 0.3f;

        [Tooltip("점멸 가속 정도. 0이면 등속, 클수록 나중 점멸이 빨라진다.")]
        [Range(0f, 5f)]
        [SerializeField] float blinkAcceleration = 2f;

        [Tooltip("안정 발광까지 올라가는 시간(초).")]
        [SerializeField] float stabilizeDuration = 1.0f;

        [Tooltip("컷씬 종료 후 영구 유지할 발광 세기(0~1).")]
        [Range(0f, 1f)]
        [SerializeField] float persistentGlowIntensity = 1f;

        [Header("03. 빛 입자")]
        [Tooltip("파티클 활성화 연출 시간(초).")]
        [SerializeField] float particleFadeDuration = 2.5f;

        [Tooltip("파티클이 완전히 활성화된 후 보여주는 시간(초).")]
        [SerializeField] float particleHoldDuration = 1.5f;

        [Header("효과음")]
        [Tooltip("아웃라인 점멸이 시작될 때 재생할 사운드.")]
        [SerializeField] AudioClip energyHumClip;
        [Range(0f, 1f)]
        [SerializeField] float energyHumVolume = 0.7f;

        [Header("카메라")]
        [SerializeField] bool followYaw = true;
        [SerializeField] bool followPitch;

        [Header("카메라 전진")]
        [Tooltip("컷씬 중 패널 쪽으로 카메라가 천천히 전진한다.")]
        [SerializeField] bool enableCameraPushIn = true;

        [Tooltip("총 전진 거리(m). 0이면 전진하지 않는다.")]
        [SerializeField] float cameraPushDistance = 0.5f;

        [Tooltip("전진 보간 커브. X축=시간(0~1), Y축=이동 비율(0~1).")]
        [SerializeField] AnimationCurve cameraPushEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("연출 중 화면 정리")]
        [SerializeField] bool hideRemoteAvatars = true;
        [SerializeField] bool lockLocomotion = true;
        [SerializeField] bool hideTutorialCanvas = true;

        [Header("Debug")]
        [SerializeField] bool isPlaying;
        [SerializeField] bool debugLog;

        Camera runtimeCamera;
        Transform xrHead;
        Transform xrOrigin;
        ScreenFade screenFade;
        HardwareRig hardwareRig;

        Vector3 originalXROriginPosition;
        Quaternion originalXROriginRotation;
        bool originalXRTransformSaved;

        Vector3 currentShotPosition;
        Quaternion currentShotRotation;
        bool hasCurrentShot;

        readonly ResonanceCutsceneOverride resonance = new ResonanceCutsceneOverride();

        SolarPanelRepairGlow[] persistentGlows;

        Vector3 cameraPushOffset;

        bool hasPlayed;
        bool xrReady;
        bool lateJoinApplied;
        bool missionClearPending;
        bool energyHumStarted;
        bool blinkCompleted;

        [Header("가동음 AudioSource (3D)")]
        [Tooltip("SolarFacilityEnergyHum 오브젝트의 AudioSource. " +
                 "Re_Bloom/Stage3/Setup Solar Facility Energy Hum 메뉴로 자동 연결.")]
        [SerializeField] AudioSource energyHumAudioSource;

        public bool IsPlaying => isPlaying;

        void Log(string msg)
        {
            if (!debugLog) return;
            Debug.Log($"[Stage3Cutscene] F{Time.frameCount} T{Time.realtimeSinceStartup:F3} FacilityClear - {msg}", this);
        }

        // ============================================================
        // Initialize
        // ============================================================

        IEnumerator Start()
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
                Debug.LogError("[FacilityClearCutscene] XR Origin을 찾지 못했습니다.", this);
                yield break;
            }

            screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
            hardwareRig = FindFirstObjectByType<HardwareRig>();
            xrReady = true;
            Log($"Start: xrReady=true, missionClearPending={missionClearPending}, hasPlayed={hasPlayed}");

            if (missionClearPending)
            {
                Log("Start: missionClearPending=true → StartMissionClearSequence 호출");
                missionClearPending = false;
                StartMissionClearSequence();
            }

            if (!hasPlayed && !lateJoinApplied)
            {
                SolarMissionManager mgr = FindFirstObjectByType<SolarMissionManager>();
                if (mgr != null && mgr.Object != null && mgr.Object.IsValid && mgr.IsClear)
                {
                    Log("Start: XR 초기화 후 IsClear=true 감지 → LateJoinCleared 이벤트 미수신 보정");
                    OnLateJoinCleared();
                }
            }
        }

        void OnEnable()
        {
            SolarMissionManager.Cleared += OnMissionCleared;
            SolarMissionManager.LateJoinCleared += OnLateJoinCleared;
            Log("OnEnable: Cleared + LateJoinCleared 이벤트 구독 완료");
        }

        void OnDisable()
        {
            SolarMissionManager.Cleared -= OnMissionCleared;
            SolarMissionManager.LateJoinCleared -= OnLateJoinCleared;
            StopAllCoroutines();
            StopEffectSounds();

            if (isPlaying)
                ForceCleanup();
        }

        void OnDestroy()
        {
            StopEffectSounds();
            CleanupPersistentGlows();
        }

        void LateUpdate()
        {
            if (isPlaying && hasCurrentShot)
                ApplyShotTransform();
        }

        // ============================================================
        // Event
        // ============================================================

        void OnMissionCleared()
        {
            Log($"OnMissionCleared: hasPlayed={hasPlayed}, isPlaying={isPlaying}, xrReady={xrReady}, lateJoinApplied={lateJoinApplied}");

            if (hasPlayed || isPlaying)
            {
                Log($"OnMissionCleared: 무시됨 (hasPlayed={hasPlayed}, isPlaying={isPlaying})");
                return;
            }

            hasPlayed = true;
            if (!xrReady)
            {
                missionClearPending = true;
                Log("OnMissionCleared: xrReady=false → missionClearPending=true 설정 후 대기");
                return;
            }

            Log("OnMissionCleared: → StartMissionClearSequence 호출");
            StartMissionClearSequence();
        }

        void StartMissionClearSequence()
        {
            Log("StartMissionClearSequence: WaitAndPlay 코루틴 시작");
            StartCoroutine(WaitAndPlay());
        }

        IEnumerator WaitAndPlay()
        {
            Log($"WaitAndPlay 진입: repairCutscene={repairCutscene != null}, IsBusy={repairCutscene?.IsBusy}");

            if (repairCutscene != null)
            {
                if (repairCutscene.IsBusy)
                    Log("WaitAndPlay: repairCutscene.IsBusy=true → 대기 시작");

                while (repairCutscene.IsBusy)
                    yield return null;

                Log("WaitAndPlay: repairCutscene.IsBusy=false → 대기 종료");
            }

            // Stage3EndingCutscene이 이미 시작됐으면 컷씬 충돌 회피
            Stage3EndingCutscene ending = FindFirstObjectByType<Stage3EndingCutscene>();
            if (ending != null && ending.IsPlaying)
            {
                Log("WaitAndPlay: Stage3EndingCutscene 재생 중 → ApplyPersistentState만 적용, 컷씬 건너뜀");
                ApplyPersistentState();
                yield break;
            }

            Log("WaitAndPlay: → PlayRoutine(preview=false) 시작");
            yield return PlayRoutine(false);
            Log("WaitAndPlay: PlayRoutine 완료");
        }

        // ============================================================
        // Late Join
        // ============================================================

        void OnLateJoinCleared()
        {
            Log($"OnLateJoinCleared: lateJoinApplied={lateJoinApplied}, hasPlayed={hasPlayed}");

            if (lateJoinApplied || hasPlayed)
            {
                Log("OnLateJoinCleared: 무시됨 (이미 처리됨)");
                return;
            }

            lateJoinApplied = true;
            hasPlayed = true;

            Log("OnLateJoinCleared: → ForceRepairAllPanels + ApplyPersistentState (컷씬 없이 영구 상태만 적용)");
            ForceRepairAllPanels();
            ApplyPersistentState();
        }

        void ForceRepairAllPanels()
        {
            foreach (PanelEntry entry in panels)
            {
                if (entry?.reflector == null) continue;
                if (!entry.reflector.broken) continue;

                // broken을 직접 꺼서 Repaired 이벤트를 발생시키지 않는다.
                // 이렇게 하면 개별 복구 컷씬이 중복 발생하지 않는다.
                entry.reflector.broken = false;

                if (entry.cleanView != null)
                    entry.cleanView.SetHoldVisual(false);
            }
        }

        void ApplyPersistentState()
        {
            Log($"ApplyPersistentState: panels={panels.Length}, persistentGlowIntensity={persistentGlowIntensity}");
            SetupPersistentGlows(persistentGlowIntensity);
            PlayEnergyHum();

            foreach (PanelEntry entry in panels)
            {
                if (entry?.particle != null)
                    entry.particle.Play();
            }

            if (teleporterCutscene != null)
                teleporterCutscene.ActivateForLateJoin();
        }

        // ============================================================
        // Cutscene
        // ============================================================

        IEnumerator PlayRoutine(bool preview)
        {
            Log($"PlayRoutine 진입: preview={preview}, shotPoint={shotPoint != null}, panels={panels.Length}");
            isPlaying = true;
            ResetEffectPlayedFlags();
            StopEffectSounds();

            // --- 이동 잠금, UI 숨김 ---
            if (lockLocomotion && hardwareRig != null)
                hardwareRig.SetLocomotionLocked(true);
            SetRemoteAvatarsVisible(false);
            SetTutorialCanvasVisible(false);

            // --- 01. 전체 시설 조망 ---
            Log("PlayRoutine: 01. FadeOut 시작");
            yield return FadeOut();

            resonance.Disable(true);
            SaveOriginalXRTransform();
            Log($"PlayRoutine: 01. XR Origin 저장 완료, pos={originalXROriginPosition}");

            SetCurrentShotTransform(shotPoint.position, shotPoint.rotation);
            Log($"PlayRoutine: 01. ShotPoint 이동 완료, shotPos={shotPoint.position}");

            // 빔을 하늘로 정렬 — 컷씬 중 패널 위에 서 있어도 aimAtSunWhenIdle로 하늘 방향 보장
            ForceBeamsToSky(true);

            yield return FadeIn();
            Log("PlayRoutine: 01. FadeIn 완료, 시설 조망 시작");

            // --- 카메라 전진 시작 (병렬) ---
            float pushDuration = viewHold + blinkDuration + particleFadeDuration;
            if (enableCameraPushIn && cameraPushDistance > 0f)
                StartCoroutine(CameraPushRoutine(pushDuration));

            // --- 02. 아웃라인 발광 ---
            Log("PlayRoutine: 02. 아웃라인 발광 시작");
            SetupPersistentGlows(0f);
            blinkCompleted = false;
            StartCoroutine(BlinkRoutine());

            if (viewHold > 0f)
                yield return new WaitForSeconds(viewHold);

            while (!blinkCompleted)
                yield return null;

            // --- 03. 빛 입자 활성화 (아웃라인 안정화와 병렬) ---
            Log("PlayRoutine: 03. 파티클 활성화 + 아웃라인 안정화 시작");
            StartCoroutine(StabilizeRoutine());
            yield return ParticleFadeInRoutine();

            // --- 03b. 파티클 유지 ---
            if (particleHoldDuration > 0f)
                yield return new WaitForSeconds(particleHoldDuration);

            // --- 04. 컷씬 종료 ---
            Log("PlayRoutine: 04. 컷씬 종료 FadeOut 시작");
            yield return FadeOut();

            hasCurrentShot = false;
            cameraPushOffset = Vector3.zero;
            RestoreOriginalXRTransform();

            if (!preview && teleporterCutscene != null)
            {
                Log($"PlayRoutine: 텔레포터 컷씬 연속 재생 시작 (teleporterCutscene={teleporterCutscene.name})");
                yield return StartCoroutine(
                    teleporterCutscene.PlayInSequenceRoutine());
                Log("PlayRoutine: 텔레포터 컷씬 연속 재생 완료");
            }
            else
            {
                Log($"PlayRoutine: 텔레포터 컷씬 건너뜀 (preview={preview}, teleporterCutscene={teleporterCutscene != null})");
            }

            resonance.Restore();
            SetRemoteAvatarsVisible(true);
            SetTutorialCanvasVisible(true);

            if (lockLocomotion && hardwareRig != null)
                hardwareRig.SetLocomotionLocked(false);

            if (preview || teleporterCutscene == null)
            {
                yield return null;
                yield return FadeIn();
            }

            isPlaying = false;
            Log($"PlayRoutine 종료: preview={preview}");

            if (preview)
                StopEffectSounds();
        }

        // ============================================================
        // 02. Glow — 깜빡임 → 안정
        // ============================================================

        void SetupPersistentGlows(float intensity)
        {
            if (persistentGlows != null) return;

            persistentGlows = new SolarPanelRepairGlow[panels.Length];

            for (int i = 0; i < panels.Length; i++)
            {
                PanelEntry entry = panels[i];
                if (entry?.cleanView == null) continue;

                SolarPanelRepairGlow glow = new SolarPanelRepairGlow();
                glow.Setup(entry.cleanView, glowColor);
                glow.SetIntensity(intensity);
                persistentGlows[i] = glow;
            }
        }

        IEnumerator BlinkRoutine()
        {
            if (persistentGlows == null || blinkCount <= 0)
            {
                blinkCompleted = true;
                yield break;
            }

            if (blinkStartDelay > 0f)
                yield return new WaitForSeconds(blinkStartDelay);

            PlayEnergyHum();

            // 점멸 주기 분배: blinkAcceleration이 클수록 첫 점멸이 길고 마지막이 짧다.
            float[] durations = new float[blinkCount];
            float weightSum = 0f;

            for (int i = 0; i < blinkCount; i++)
            {
                float progress = blinkCount > 1 ? (float)(blinkCount - 1 - i) / (blinkCount - 1) : 0f;
                float weight = 1f + blinkAcceleration * progress;
                durations[i] = weight;
                weightSum += weight;
            }

            for (int i = 0; i < blinkCount; i++)
                durations[i] = durations[i] / weightSum * blinkDuration;

            for (int b = 0; b < blinkCount; b++)
            {
                bool isLast = b == blinkCount - 1;

                if (isLast)
                {
                    float elapsed = 0f;
                    while (elapsed < durations[b])
                    {
                        elapsed += Time.deltaTime;
                        float t = Mathf.SmoothStep(0.1f, 1f, elapsed / durations[b]);
                        SetAllGlowIntensity(t);
                        yield return null;
                    }
                    SetAllGlowIntensity(1f);
                }
                else
                {
                    float halfBlink = durations[b] * 0.5f;

                    float elapsed = 0f;
                    while (elapsed < halfBlink)
                    {
                        elapsed += Time.deltaTime;
                        float t = Mathf.SmoothStep(0f, 0.8f, elapsed / halfBlink);
                        SetAllGlowIntensity(t);
                        yield return null;
                    }

                    elapsed = 0f;
                    while (elapsed < halfBlink)
                    {
                        elapsed += Time.deltaTime;
                        float t = Mathf.SmoothStep(0.8f, 0.1f, elapsed / halfBlink);
                        SetAllGlowIntensity(t);
                        yield return null;
                    }
                }
            }

            blinkCompleted = true;
        }

        IEnumerator StabilizeRoutine()
        {
            if (persistentGlows == null) yield break;

            float startIntensity = 1f;
            float elapsed = 0f;

            while (elapsed < stabilizeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(startIntensity, persistentGlowIntensity,
                    elapsed / stabilizeDuration);
                SetAllGlowIntensity(t);
                yield return null;
            }

            SetAllGlowIntensity(persistentGlowIntensity);
        }

        void SetAllGlowIntensity(float intensity)
        {
            if (persistentGlows == null) return;

            foreach (SolarPanelRepairGlow glow in persistentGlows)
                glow?.SetIntensity(intensity);
        }

        void CleanupPersistentGlows()
        {
            if (persistentGlows == null) return;

            foreach (SolarPanelRepairGlow glow in persistentGlows)
                glow?.Cleanup();

            persistentGlows = null;
        }

        // ============================================================
        // 02/03. Effects Audio
        // ============================================================

        void ResetEffectPlayedFlags()
        {
            energyHumStarted = false;
            blinkCompleted = false;
        }

        void PlayEnergyHum()
        {
            if (energyHumStarted) return;

            if (energyHumAudioSource == null)
            {
                Debug.LogWarning(
                    "[FacilityClearCutscene] energyHumAudioSource가 연결되지 않았습니다. " +
                    "Re_Bloom/Stage3/Setup Solar Facility Energy Hum 메뉴를 실행하세요.", this);
                return;
            }

            if (energyHumClip == null)
            {
                Debug.LogWarning("[FacilityClearCutscene] EnergyHum AudioClip이 설정되지 않았습니다.", this);
                return;
            }

            energyHumStarted = true;

            if (energyHumAudioSource.isPlaying)
                return;

            energyHumAudioSource.loop = true;
            energyHumAudioSource.volume = energyHumVolume;
            energyHumAudioSource.clip = energyHumClip;
            energyHumAudioSource.Play();
        }

        void StopEffectSounds()
        {
            energyHumAudioSource?.Stop();
        }

        // ============================================================
        // 03. Particles
        // ============================================================

        IEnumerator ParticleFadeInRoutine()
        {
            float interval = panels.Length > 0
                ? particleFadeDuration / panels.Length
                : particleFadeDuration;

            Log($"ParticleFadeInRoutine: panels={panels.Length}, interval={interval:F3}s");

            for (int i = 0; i < panels.Length; i++)
            {
                PanelEntry entry = panels[i];
                if (entry?.particle != null)
                {
                    entry.particle.Play();
                    Log($"ParticleFadeInRoutine: 패널[{i}] particle.Play() 호출, " +
                        $"IsPlaying={entry.particle.IsPlaying}");
                }
                else
                {
                    Log($"ParticleFadeInRoutine: 패널[{i}] particle 참조 없음 (null)");
                }

                if (interval > 0f)
                    yield return new WaitForSeconds(interval);
            }

            Log("ParticleFadeInRoutine: 완료");
        }

        // ============================================================
        // Camera Push
        // ============================================================

        IEnumerator CameraPushRoutine(float duration)
        {
            Vector3 pushDir = ComputePushDirection();
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cameraPushOffset = pushDir * (cameraPushEase.Evaluate(t) * cameraPushDistance);
                yield return null;
            }

            cameraPushOffset = pushDir * cameraPushDistance;
        }

        Vector3 ComputePushDirection()
        {
            Vector3 center = Vector3.zero;
            int count = 0;

            foreach (PanelEntry entry in panels)
            {
                if (entry?.reflector == null) continue;
                center += entry.reflector.transform.position;
                count++;
            }

            if (count == 0 || shotPoint == null)
                return shotPoint != null ? shotPoint.forward : Vector3.forward;

            center /= count;

            Vector3 dir = center - shotPoint.position;
            dir.y = 0f;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : shotPoint.forward;
        }

        // ============================================================
        // Beam Sky Alignment
        // ============================================================

        void ForceBeamsToSky(bool force)
        {
            foreach (PanelEntry entry in panels)
            {
                if (entry?.reflector == null) continue;
                SolarBeam beam = entry.reflector.GetComponent<SolarBeam>();
                if (beam != null)
                    beam.aimAtSunWhenIdle = true;
            }
        }

        // ============================================================
        // XR Shot
        // ============================================================

        void ApplyShotTransform()
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

            xrOrigin.position += (currentShotPosition + cameraPushOffset) - xrHead.position;
        }

        void SetCurrentShotTransform(Vector3 position, Quaternion rotation)
        {
            currentShotPosition = position;
            currentShotRotation = rotation;
            hasCurrentShot = true;
            ApplyShotTransform();
        }

        void SaveOriginalXRTransform()
        {
            if (xrOrigin == null || originalXRTransformSaved) return;
            originalXROriginPosition = xrOrigin.position;
            originalXROriginRotation = xrOrigin.rotation;
            originalXRTransformSaved = true;
        }

        void RestoreOriginalXRTransform()
        {
            if (xrOrigin == null || !originalXRTransformSaved) return;
            xrOrigin.position = originalXROriginPosition;
            xrOrigin.rotation = originalXROriginRotation;
            originalXRTransformSaved = false;
        }

        // ============================================================
        // Fade
        // ============================================================

        IEnumerator FadeOut()
        {
            if (screenFade != null)
                yield return StartCoroutine(screenFade.FadeOut(fadeDuration));
        }

        IEnumerator FadeIn()
        {
            if (screenFade != null)
                yield return StartCoroutine(screenFade.FadeIn(fadeDuration));
        }

        // ============================================================
        // Avatar / UI
        // ============================================================

        void SetRemoteAvatarsVisible(bool visible)
        {
            if (!hideRemoteAvatars) return;

            foreach (NetworkPlayer player in NetworkPlayer.All)
            {
                if (player != null && !player.IsLocalNetworkRig)
                    player.SetAvatarVisible(visible);
            }
        }

        void SetTutorialCanvasVisible(bool visible)
        {
            if (hideTutorialCanvas && hardwareRig != null)
                hardwareRig.SetTutorialCanvasVisible(visible);
        }

        // ============================================================
        // Safety Restore
        // ============================================================

        void ForceCleanup()
        {
            Log("ForceCleanup: 비상 정리 실행");
            isPlaying = false;
            hasCurrentShot = false;
            cameraPushOffset = Vector3.zero;
            StopEffectSounds();

            RestoreOriginalXRTransform();
            resonance.Restore();

            SetRemoteAvatarsVisible(true);
            SetTutorialCanvasVisible(true);

            if (lockLocomotion && hardwareRig != null)
                hardwareRig.SetLocomotionLocked(false);
        }

        [ContextMenu("Play Clear Cutscene")]
        internal void ContextPlayCutscene()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FacilityClearCutscene] Play 모드에서만 실행할 수 있습니다.");
                return;
            }

            if (isPlaying)
            {
                Debug.LogWarning("[FacilityClearCutscene] 컷씬이 이미 재생 중입니다.");
                return;
            }

            hasPlayed = true;
            StartCoroutine(PlayRoutine(true));
        }

#if UNITY_EDITOR
        // ============================================================
        // Editor Test
        // ============================================================

        /// <summary>Play 모드에서 미션 클리어 없이 컷씬을 테스트한다.</summary>
        public void EditorTestPlay()
        {
            if (!Application.isPlaying) return;
            if (isPlaying) return;

            hasPlayed = true;
            StartCoroutine(PlayRoutine(true));
        }

        /// <summary>Play 모드에서 영구 상태만 즉시 적용한다(컷씬 없이).</summary>
        public void EditorTestApplyPersistent()
        {
            if (!Application.isPlaying) return;
            ApplyPersistentState();
        }
#endif
    }
}
