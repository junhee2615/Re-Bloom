using System.Collections;
using FlatKit;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives the local player's FlatKit fog and the Resonance post-processing Volume
/// (weight + Bloom/Vignette) from the co-op constraint state, without synchronizing
/// renderer or material state. The networked tutorial state is only a local trigger.
/// </summary>
public sealed class ResonanceController : MonoBehaviour
{
    private static readonly int DistanceFogIntensityId = Shader.PropertyToID("_DistanceFogIntensity");
    private static readonly int HeightFogIntensityId = Shader.PropertyToID("_HeightFogIntensity");

    [Header("Fog")]
    [SerializeField] private FogSettings fogSettings;
    [SerializeField, Range(0f, 1f)] private float activeIntensity = 1f;
    [SerializeField, Min(0f)] private float transitionDuration = 1.5f;
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Distance-Based Constraint")]
    [SerializeField] private Transform hostSpawnPoint;
    [SerializeField] private Transform clientSpawnPoint;
    [SerializeField, Min(0f)] private float minDistance = 2f;
    [SerializeField, Range(0f, 0.99f)] private float maxConstraintRelief = 0.7f; // 완화 최대 비율
    [SerializeField, Min(0f)] private float distanceResponseSpeed = 6f;
    [Tooltip("거리 계산과 Fog 적용값을 매 프레임 출력합니다. 원인 확인 후 끄세요.")]
    [SerializeField] private bool logDistanceCalculation;
    private float resonanceDistance = 15f; // 스폰 포인트간 거리

    [Header("Networked Resonance Trigger")]
    [Tooltip("Networked Tutorial state가 각 클라이언트에 도착할 때 로컬 Fog를 전환합니다.")]
    [SerializeField] private bool useNetworkedTutorialState = true;
    [SerializeField] private TutorialStep activateStep = TutorialStep.GeneratorComplete; // 거리 멀어지면
    [SerializeField] private TutorialStep deactivateStep = TutorialStep.ValveComplete; // 활성화 모션
    [SerializeField, Min(0f)] private float lateJoinStateWaitSeconds = 10f;

    [Header("Constraint Post-Processing")]
    [Tooltip("Resonance Global Volume.")]
    [SerializeField] private Volume resonanceVolume;
    [Tooltip("공명 활성화 상태의 Volume weight.")]
    [SerializeField, Range(0f, 1f)] private float releasedVolumeWeight = 0.6f;
    [Tooltip("가까워져 제약이 완화됐을 때의 Volume weight.")]
    [SerializeField, Range(0f, 1f)] private float relievedVolumeWeight = 0.9f;
    private const float constrainedVolumeWeight = 1f;

    [Header("Constraint Audio")]
    [Tooltip("제약 시 Resonance 그룹, 해제 시 Normal(Master) 그룹으로 출력을 전환할 AudioSource들.")]
    [SerializeField] private AudioSource[] constrainedAudioSources;
    [Tooltip("제약 상태의 믹서 그룹 (Resonance).")]
    [SerializeField] private AudioMixerGroup resonanceGroup;
    [Tooltip("제약 해제 상태의 믹서 그룹 (Master).")]
    [SerializeField] private AudioMixerGroup normalGroup;

    [Header("Debug")]
    [Tooltip("테스트용: 끄면 안개·Volume 제약 연출을 모두 비활성화한다. 플레이 중 실시간 토글 가능.")]
    [SerializeField] private bool constraintEnabled = true;

    private Material fogMaterial;
    private Coroutine transitionCoroutine;
    private float configuredDistanceIntensity;
    private float configuredHeightIntensity;
    private float currentIntensity;
    private float fogVisibility = 1f; // ON or OFF 이벤트 발생시 Fade 적용 용도
    private bool initialized;
    private bool receivedNetworkState;
    private bool isConstraintReleased;

    private Bloom bloomOverride;
    private Vignette vignetteOverride;
    private float configuredBloomIntensity;
    private float configuredVignetteIntensity;
    private float smoothedVolumeWeight;
    private bool volumeInitialized;

    // 제약 연출이 꺼진 상태
    private bool IsConstraintInactive => !constraintEnabled || isConstraintReleased;

    private void Awake()
    {
        InitializeResonanceDistance();
        InitializeFog();
        InitializeVolume();

        ApplyConstraintEffects(); // Bloom/Vignette + 청각 초기 상태
        ApplyIntensity(constraintEnabled ? activeIntensity : 0f);
    }

    private void OnEnable()
    {
        CooperativeActivationController.ActivationSucceeded += ReleaseFogConstraint;

        if (!useNetworkedTutorialState) return;

        TutorialMissionManager.TutorialChanged += OnTutorialChanged;
        StartCoroutine(ApplyLateJoinState());
    }

    private void OnDisable()
    {
        CooperativeActivationController.ActivationSucceeded -= ReleaseFogConstraint;
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;

        StopAllCoroutines();

        if (Application.isPlaying)
        {
            ApplyIntensity(0f);
        }
    }

    private void OnDestroy()
    {
        RestoreMaterialValues();
    }

    private void OnApplicationQuit()
    {
        RestoreMaterialValues();
    }

#if UNITY_EDITOR
    // 플레이 중 인스펙터 토글 즉시 반영.
    private void OnValidate()
    {
        if (!Application.isPlaying || !volumeInitialized) return;

        if (!constraintEnabled)
        {
            ApplyIntensity(0f);
            smoothedVolumeWeight = releasedVolumeWeight;
            resonanceVolume.weight = releasedVolumeWeight;
        }
        ApplyConstraintEffects();
    }
#endif

    private void SetFogActive(bool active)
    {
        if (!initialized)
            return;
        
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        // Update is the sole writer of the material intensity. The transition
        // only changes this visibility multiplier, so it cannot overwrite a
        // distance-based value later in the same frame.
        transitionCoroutine = StartCoroutine(TransitionVisibilityTo(active ? 1f : 0f));
    }

    public void ActivateFog()
    {
        Debug.Log("Fog 활성화");
        SetFogActive(true);
    }

    public void DeactivateFog()
    {
        Debug.Log("Fog 비활성화");        
        SetFogActive(false);
    }

    private void InitializeFog()
    {
        if (initialized) return;

        if (fogSettings == null || fogSettings.effectMaterial == null)
        {
            Debug.LogError("ResonanceController에 FlatKit FogSettings와 Effect Material이 필요합니다.", this);
            return;
        }

        fogMaterial = fogSettings.effectMaterial;
        configuredDistanceIntensity = fogSettings.distanceFogIntensity;
        configuredHeightIntensity = fogSettings.heightFogIntensity;
        initialized = true;
    }

    private void InitializeVolume()
    {
        if (volumeInitialized) return;

        if (resonanceVolume == null)
        {
            Debug.LogWarning("ResonanceController에 Resonance Global Volume이 지정되지 않았습니다. 제약 포스트 프로세싱 연출을 건너뜁니다.", this);
            return;
        }

        // Volume.profile은 공유 에셋 대신 런타임 인스턴스 복제본을 반환한다.
        // 여기서 Bloom/Vignette 값을 바꿔도 원본 프로파일 에셋은 보존된다.
        VolumeProfile runtimeProfile = resonanceVolume.profile;

        if (runtimeProfile.TryGet(out bloomOverride))
        {
            configuredBloomIntensity = bloomOverride.intensity.value;
            bloomOverride.active = true;
            bloomOverride.intensity.overrideState = true;
        }
        
        if (runtimeProfile.TryGet(out vignetteOverride))
        {
            configuredVignetteIntensity = vignetteOverride.intensity.value;
            vignetteOverride.active = true;
            vignetteOverride.intensity.overrideState = true;
        }

        // 시작 weight
        float startWeight = constraintEnabled ? constrainedVolumeWeight : releasedVolumeWeight;
        resonanceVolume.weight = startWeight;
        smoothedVolumeWeight = startWeight;
        volumeInitialized = true;
    }

    /// <summary>
    /// Volume weight를 거리(proximity)로 구동한다.
    /// 멀리 = constrainedVolumeWeight(1), 가까움 = relievedVolumeWeight(0.9),
    /// 공명 해제 = releasedVolumeWeight(0.6).
    /// </summary>
    private void UpdateVolumeWeight(bool hasDistance, float playerDistance)
    {
        if (!volumeInitialized) return;

        float targetWeight = IsConstraintInactive ? releasedVolumeWeight
            : Mathf.Lerp(constrainedVolumeWeight, relievedVolumeWeight, CalculateProximity(hasDistance, playerDistance));

        smoothedVolumeWeight = Mathf.Lerp(smoothedVolumeWeight, targetWeight, DistanceInterpolation());
        resonanceVolume.weight = smoothedVolumeWeight;
    }

    /// <summary>
    /// 공명 on/off에 따라 Bloom/Vignette를 껐다 켠다. 상태가 바뀔 때만 호출한다.
    /// (weight와 곱해 이중으로 옅어지지 않도록 0 <-> 설정값 이진 전환)
    /// </summary>
    private void ApplyPostFx()
    {
        if (!volumeInitialized) return;

        float postFx = IsConstraintInactive ? 0f : 1f;
        if (bloomOverride != null)
            bloomOverride.intensity.value = configuredBloomIntensity * postFx;
        if (vignetteOverride != null)
            vignetteOverride.intensity.value = configuredVignetteIntensity * postFx;
    }

    /// <summary>
    /// 공명 on/off에 따라 지정 AudioSource의 출력 믹서 그룹을 전환한다.
    /// 제약 = resonanceGroup, 해제 = Master.
    /// </summary>
    private void ApplyAudioConstraint()
    {
        if (constrainedAudioSources == null) return;

        AudioMixerGroup target = IsConstraintInactive ? normalGroup : resonanceGroup;
        foreach (AudioSource source in constrainedAudioSources)
            if (source != null)
                source.outputAudioMixerGroup = target;
    }

    // 공명 on/off에 따른 효과(Bloom/Vignette + 청각)를 반영.
    private void ApplyConstraintEffects()
    {
        ApplyPostFx();
        ApplyAudioConstraint();
    }

    private void InitializeResonanceDistance()
    {
        if (hostSpawnPoint == null || clientSpawnPoint == null)
        {
            Debug.LogWarning($"[Resonance] Spawn point가 지정되지 않아 기본 거리({resonanceDistance})를 사용합니다.", this);
            return;
        }

        resonanceDistance = Vector3.Distance(hostSpawnPoint.position, clientSpawnPoint.position);
        Debug.Log($"[Resonance Fog] Spawn-point resonance distance initialized: {resonanceDistance:F2}", this);
    }

    private void Update()
    {
        if (!initialized || !constraintEnabled)
            return;

        // 테스트 토글 OFF: Update 전체를 쉰다. OFF 스냅은 Awake/OnValidate에서 처리.
        // if (!constraintEnabled)
        //     return;

        bool hasDistance = TryGetOtherPlayerDistance(out float playerDistance);

        // 제약 해제 상태를 되돌리고 네트워크 공명 성공 상태도 리셋
        if (isConstraintReleased && hasDistance && playerDistance >= resonanceDistance)
            ReengageConstraint();

        float targetIntensity = isConstraintReleased ? 0f
            : CalculateDistanceConstraintIntensity(hasDistance, playerDistance);

        targetIntensity *= fogVisibility;

        ApplyIntensity(Mathf.Lerp(currentIntensity, targetIntensity, DistanceInterpolation()));

        UpdateVolumeWeight(hasDistance, playerDistance);

        if (logDistanceCalculation)
        {
            Debug.Log(
                $"targetIntensity={targetIntensity:F3}, currentIntensity={currentIntensity:F3}, " +
                $"activeIntensity={activeIntensity:F3}, visibility={fogVisibility:F3}", this);
        }
    }

    /// <summary>
    /// 두 플레이어 거리로 근접도[0..1]를 구한다.
    /// 0 = 공명 거리(멀리), 1 = minDistance 이내(가장 가까움).
    /// </summary>
    private float CalculateProximity(bool hasDistance, float playerDistance)
    {
        if (!hasDistance) return 0f;

        float clampedMinDistance = Mathf.Min(minDistance, resonanceDistance - 0.001f);
        return Mathf.InverseLerp(resonanceDistance, clampedMinDistance, playerDistance);
    }

    // distanceResponseSpeed 기반 프레임 독립 보간 계수.
    private float DistanceInterpolation()
    {
        return distanceResponseSpeed <= 0f
            ? 1f : 1f - Mathf.Exp(-distanceResponseSpeed * Time.deltaTime);
    }

    private float CalculateDistanceConstraintIntensity(bool hasDistance, float playerDistance)
    {
        // maxConstraintRelief is capped below 1 in the Inspector, so the fog
        // constraint can never be fully removed merely by getting closer.
        float relief = maxConstraintRelief * CalculateProximity(hasDistance, playerDistance);
        return activeIntensity * (1f - relief);
    }

    private static bool TryGetOtherPlayerDistance(out float playerDistance)
    {
        playerDistance = 0f;

        NetworkPlayer localPlayer = null;
        NetworkPlayer remotePlayer = null;
        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            if (player.IsLocalNetworkRig)
                localPlayer = player;
            else
                remotePlayer ??= player;
        }

        if (localPlayer == null || remotePlayer == null || remotePlayer.PlayerTransform == null)
            return false;

        // The local NetworkTransform can lag behind the XR rig in simulator and
        // in a network tick.  Use the real local rig position first, while the
        // remote player's replicated NetworkTransform remains the source for
        // the other player.
        Transform localTransform = localPlayer.HardwareRig != null
            ? localPlayer.HardwareRig.playerTransform
            : null;

        if (localTransform == null)
            localTransform = localPlayer.PlayerTransform;

        if (localTransform == null)
            return false;
        
        playerDistance = Vector3.Distance(localTransform.position, remotePlayer.PlayerTransform.position);
        return true;
    }

    private IEnumerator TransitionVisibilityTo(float targetVisibility)
    {
        float startVisibility = fogVisibility;
        if (transitionDuration <= 0f)
        {
            fogVisibility = targetVisibility;
            transitionCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / transitionDuration);
            float curvedTime = transitionCurve == null
                ? normalizedTime : transitionCurve.Evaluate(normalizedTime);
            fogVisibility = Mathf.LerpUnclamped(startVisibility, targetVisibility, curvedTime);
            yield return null;
        }

        fogVisibility = targetVisibility;
        transitionCoroutine = null;
    }

    private void ApplyIntensity(float multiplier)
    {
        if (!initialized || fogMaterial == null) return;

        currentIntensity = Mathf.Max(0f, multiplier);
        float distanceIntensity = configuredDistanceIntensity * currentIntensity;
        float heightIntensity = configuredHeightIntensity * currentIntensity;

        // FlatKit copies FogSettings values when its renderer feature is (re)created,
        // so both sources stay aligned. Runtime changes are restored when play ends.
        fogSettings.distanceFogIntensity = distanceIntensity;
        fogSettings.heightFogIntensity = heightIntensity;
        fogMaterial.SetFloat(DistanceFogIntensityId, distanceIntensity);
        fogMaterial.SetFloat(HeightFogIntensityId, heightIntensity);
    }

    private void RestoreMaterialValues()
    {
        if (!initialized || fogMaterial == null) return;

        fogSettings.distanceFogIntensity = configuredDistanceIntensity;
        fogSettings.heightFogIntensity = configuredHeightIntensity;
        fogMaterial.SetFloat(DistanceFogIntensityId, configuredDistanceIntensity);
        fogMaterial.SetFloat(HeightFogIntensityId, configuredHeightIntensity);
    }

    private void OnTutorialChanged(TutorialStep step)
    {
        receivedNetworkState = true;

        if (step == activateStep)
            ActivateFog();
        else if (step == deactivateStep)
            DeactivateFog();
    }

    private void ReleaseFogConstraint()
    {
        isConstraintReleased = true;
        ApplyConstraintEffects(); // 공명 해제 → Bloom/Vignette OFF, 청각 정상
    }

    /// <summary>
    /// 거리 이탈로 제약을 다시 적용한다.
    /// </summary>
    private void ReengageConstraint()
    {
        isConstraintReleased = false;
        ApplyConstraintEffects(); // 다시 제약 → Bloom/Vignette ON, 청각 제약

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player != null && player.Object != null && player.Object.IsValid)
                player.ClearCooperativeActivation();
        }
    }

    // 중간에 참가한 클라이언트가 현재 튜토리얼 상태 받기
    private IEnumerator ApplyLateJoinState()
    {
        float elapsed = 0f;

        while (!receivedNetworkState && elapsed < lateJoinStateWaitSeconds)
        {
            TutorialMissionManager manager = FindFirstObjectByType<TutorialMissionManager>();
            if (manager != null && manager.Object != null && manager.Object.IsValid)
            {
                OnTutorialChanged(manager.CurrentTutorial);
                break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}
