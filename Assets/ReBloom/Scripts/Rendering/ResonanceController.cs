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
    [SerializeField, Range(0f, 1f)] private float activeIntensity = 1f;
    [SerializeField, Min(0f)] private float transitionDuration = 1.5f;
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Networked Resonance Trigger")]
    [Tooltip("Networked Tutorial state가 각 클라이언트에 도착할 때 로컬 Fog를 전환합니다.")]
    [SerializeField] private bool useNetworkedTutorialState = true;
    [SerializeField] private TutorialStep activateOnStep = TutorialStep.GeneratorComplete;
    [SerializeField] private TutorialStep deactivateOnStep = TutorialStep.ValveComplete;
    [SerializeField, Min(0f)] private float lateJoinStateWaitSeconds = 10f;

    private Material fogMaterial;
    private Coroutine transitionCoroutine;
    private Coroutine lateJoinCoroutine;
    private float configuredDistanceIntensity;
    private float configuredHeightIntensity;
    private float currentIntensity;
    private bool initialized;
    private bool receivedNetworkState;

    private void Awake()
    {
        InitializeFog();

        // The local visual starts off on every scene entry, regardless of authority.
        ApplyIntensity(0f);
    }

    private void OnEnable()
    {
        if (!useNetworkedTutorialState)
            return;

        TutorialMissionManager.TutorialChanged += OnTutorialChanged;
        lateJoinCoroutine = StartCoroutine(ApplyLateJoinState());
    }

    private void OnDisable()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
            transitionCoroutine = null;
        }

        if (lateJoinCoroutine != null)
        {
            StopCoroutine(lateJoinCoroutine);
            lateJoinCoroutine = null;
        }

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
        if (!initialized && !InitializeFog())
            return;

        float target = active ? activeIntensity : 0f;

        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        transitionCoroutine = StartCoroutine(TransitionTo(target));
    }

    public void ActivateFog()
    {
        SetFogActive(true);
    }

    public void DeactivateFog()
    {
        SetFogActive(false);
    }

    private bool InitializeFog()
    {
        if (initialized)
            return true;

        if (fogSettings == null || fogSettings.effectMaterial == null)
        {
            Debug.LogError("ResonanceController에 FlatKit FogSettings와 Effect Material이 필요합니다.", this);
            return false;
        }

        fogMaterial = fogSettings.effectMaterial;
        configuredDistanceIntensity = fogSettings.distanceFogIntensity;
        configuredHeightIntensity = fogSettings.heightFogIntensity;
        currentIntensity = 0f;
        initialized = true;
        return true;
    }

    private IEnumerator TransitionTo(float targetIntensity)
    {
        float startIntensity = currentIntensity;
        float duration = Mathf.Max(0f, transitionDuration);

        if (duration <= 0f)
        {
            ApplyIntensity(targetIntensity);
            transitionCoroutine = null;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);
            float curvedTime = transitionCurve == null
                ? normalizedTime
                : transitionCurve.Evaluate(normalizedTime);

            ApplyIntensity(Mathf.LerpUnclamped(startIntensity, targetIntensity, curvedTime));
            yield return null;
        }

        ApplyIntensity(targetIntensity);
        transitionCoroutine = null;
    }

    private void ApplyIntensity(float multiplier)
    {
        if (!initialized || fogMaterial == null)
            return;

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
        if (!initialized || fogMaterial == null)
            return;

        fogSettings.distanceFogIntensity = configuredDistanceIntensity;
        fogSettings.heightFogIntensity = configuredHeightIntensity;
        fogMaterial.SetFloat(DistanceFogIntensityId, configuredDistanceIntensity);
        fogMaterial.SetFloat(HeightFogIntensityId, configuredHeightIntensity);
    }

    private void OnTutorialChanged(TutorialStep step)
    {
        receivedNetworkState = true;

        if (step == activateOnStep)
            ActivateFog();
        else if (step == deactivateOnStep)
            DeactivateFog();
    }

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

        lateJoinCoroutine = null;
    }
}
