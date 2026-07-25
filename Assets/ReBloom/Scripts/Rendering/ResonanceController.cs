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
    [SerializeField, Min(0.01f)] private float resonanceDistance = 15f; // Host, Client Spawn 차이
    [SerializeField, Min(0f)] private float minDistance = 2f;
    [SerializeField, Range(0f, 0.99f)] private float maxConstraintRelief = 0.7f;
    [SerializeField, Min(0f)] private float distanceResponseSpeed = 6f;

    [Header("Networked Resonance Trigger")]
    [Tooltip("Networked Tutorial state가 각 클라이언트에 도착할 때 로컬 Fog를 전환합니다.")]
    [SerializeField] private bool useNetworkedTutorialState = true;
    [SerializeField] private TutorialStep activateStep = TutorialStep.GeneratorComplete;
    [SerializeField] private TutorialStep deactivateStep = TutorialStep.ValveComplete;
    [SerializeField, Min(0f)] private float lateJoinStateWaitSeconds = 10f;

    private Material fogMaterial;
    private Coroutine transitionCoroutine;
    private float configuredDistanceIntensity;
    private float configuredHeightIntensity;
    private float currentIntensity;
    private bool initialized;
    private bool receivedNetworkState;
    // private bool distanceConstraintActive;

    private void Awake()
    {
        InitializeFog();

        ApplyIntensity(startIntensity);
    }

    private void OnEnable()
    {
        // distanceConstraintActive = !useNetworkedTutorialState;

        if (!useNetworkedTutorialState) return;

        TutorialMissionManager.TutorialChanged += OnTutorialChanged;
        StartCoroutine(ApplyLateJoinState());
    }

    private void OnDisable()
    {
        // distanceConstraintActive = false;
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

    public void SetFogActive(bool active)
    {
        if (!initialized)
            return;

        float target = active ? activeIntensity : 0f;
        
        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        // distanceConstraintActive = active;

        transitionCoroutine = StartCoroutine(TransitionTo(target));
        // if (!active)
        //     transitionCoroutine = StartCoroutine(TransitionTo(0f));
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

    private void Update()
    {
        // if (!initialized || !distanceConstraintActive)
        if (!initialized)
            return;

        float targetIntensity = CalculateDistanceConstraintIntensity();
        float interpolation = distanceResponseSpeed <= 0f
            ? 1f : 1f - Mathf.Exp(-distanceResponseSpeed * Time.deltaTime);

        ApplyIntensity(Mathf.Lerp(currentIntensity, targetIntensity, interpolation));
    }

    private float CalculateDistanceConstraintIntensity()
    {
        float relief = 0f; // 제약이 얼마나 완화되는지
        if (TryGetOtherPlayerDistance(out float playerDistance))
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
        foreach (NetworkPlayer player in FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None))
        {
            if (player == null || player.Object == null || !player.Object.IsValid ||
                player.PlayerTransform == null)
                continue;

            if (player.IsLocalNetworkRig)
                localPlayer = player;
            else
                remotePlayer ??= player;
        }

        if (localPlayer == null || remotePlayer == null) return false;

        playerDistance = Vector3.Distance(localPlayer.PlayerTransform.position, remotePlayer.PlayerTransform.position);
        return true;
    }

    private IEnumerator TransitionTo(float targetIntensity)
    {
        float start = currentIntensity;
        float duration = transitionDuration;

        if (duration <= 0f)
        {
            ApplyIntensity(targetIntensity);
            transitionCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float curvedTime = transitionCurve == null
                ? normalizedTime : transitionCurve.Evaluate(normalizedTime);

            ApplyIntensity(Mathf.LerpUnclamped(start, targetIntensity, curvedTime));
            yield return null;
        }

        ApplyIntensity(targetIntensity);
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
