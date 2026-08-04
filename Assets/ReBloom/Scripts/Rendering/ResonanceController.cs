using System.Collections;
using FlatKit;
using UnityEngine;

/// <summary>
/// Drives the local player's FlatKit fog without synchronizing renderer or material state.
/// The networked tutorial state is only used as a local trigger on each peer.
/// </summary>
public sealed class ResonanceController : MonoBehaviour
{
    private static readonly int DistanceFogIntensityId = Shader.PropertyToID("_DistanceFogIntensity");
    private static readonly int HeightFogIntensityId = Shader.PropertyToID("_HeightFogIntensity");

    [Header("Fog")]
    [SerializeField] private FogSettings fogSettings;
    [SerializeField, Range(0f, 1f)] private float startIntensity = 1f;
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

    private Material fogMaterial;
    private Coroutine transitionCoroutine;
    private float configuredDistanceIntensity;
    private float configuredHeightIntensity;
    private float currentIntensity;
    private float fogVisibility = 1f; // ON or OFF 이벤트 발생시 Fade 적용 용도
    private bool initialized;
    private bool receivedNetworkState;
    private bool isConstraintReleased;

    private void Awake()
    {
        InitializeResonanceDistance();
        InitializeFog();

        ApplyIntensity(startIntensity);
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
            ApplyIntensity(0f);
    }

    private void OnDestroy()
    {
        RestoreMaterialValues();
    }

    private void OnApplicationQuit()
    {
        RestoreMaterialValues();
    }

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
        currentIntensity = startIntensity;
        initialized = true;
    }

    private void InitializeResonanceDistance()
    {
        Transform hostPoint = hostSpawnPoint;
        Transform clientPoint = clientSpawnPoint;

        resonanceDistance = Vector3.Distance(hostPoint.position, clientPoint.position);
        Debug.Log($"[Resonance Fog] Spawn-point resonance distance initialized: {resonanceDistance:F2}", this);
    }

    private void Update()
    {
        if (!initialized)
            return;

        bool hasDistance = TryGetOtherPlayerDistance(out float playerDistance);
        
        // 제약 해제 상태를 되돌리고 네트워크 공명 성공 상태도 리셋
        if (isConstraintReleased && hasDistance && playerDistance >= resonanceDistance)
            ReengageConstraint();

        float targetIntensity = isConstraintReleased ? 0f
            : CalculateDistanceConstraintIntensity(hasDistance, playerDistance);

        targetIntensity *= fogVisibility;

        float interpolation = distanceResponseSpeed <= 0f
            ? 1f : 1f - Mathf.Exp(-distanceResponseSpeed * Time.deltaTime);

        ApplyIntensity(Mathf.Lerp(currentIntensity, targetIntensity, interpolation));

        if (logDistanceCalculation)
        {
            Debug.Log(
                $"targetIntensity={targetIntensity:F3}, currentIntensity={currentIntensity:F3}, " +
                $"activeIntensity={activeIntensity:F3}, visibility={fogVisibility:F3}", this);
        }
    }

    private float CalculateDistanceConstraintIntensity(bool hasDistance, float playerDistance)
    {
        float relief = 0f; // 제약이 얼마나 완화되는지
        if (hasDistance)
        {
            float clampedMinDistance = Mathf.Min(minDistance, resonanceDistance - 0.001f);
            float proximity = Mathf.InverseLerp(resonanceDistance, clampedMinDistance, playerDistance);
            relief = Mathf.Lerp(0f, maxConstraintRelief, proximity);
        }

        // maxConstraintRelief is capped below 1 in the Inspector, so the fog
        // constraint can never be fully removed merely by getting closer.
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
    }

    /// <summary>
    /// 거리 이탈로 제약을 다시 적용한다.
    /// </summary>
    private void ReengageConstraint()
    {
        isConstraintReleased = false;

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
