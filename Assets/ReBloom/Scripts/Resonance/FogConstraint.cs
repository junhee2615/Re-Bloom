using System;
using FlatKit;
using UnityEngine;

/// <summary>
/// Applies the FlatKit distance fog for the resonance constraint. Owns the shared
/// fog material state; the intensity is driven purely by distance proximity.
/// </summary>
[Serializable]
public sealed class FogConstraint
{
    private static readonly int DistanceFogIntensityId = Shader.PropertyToID("_DistanceFogIntensity");
    private static readonly int HeightFogIntensityId = Shader.PropertyToID("_HeightFogIntensity");

    [SerializeField] private FogSettings fogSettings;
    [SerializeField, Range(0f, 1f)] private float activeIntensity = 1f;
    [SerializeField, Range(0f, 0.99f)] private float maxConstraintRelief = 0.7f; // 완화 최대 비율

    private Material fogMaterial;
    private float configuredDistanceIntensity;
    private float configuredHeightIntensity;
    private float currentIntensity;
    private bool initialized;

    public float CurrentIntensity => currentIntensity;
    public float ActiveIntensity => activeIntensity;

    public bool Initialize(UnityEngine.Object context)
    {
        if (initialized) return true;

        if (fogSettings == null || fogSettings.effectMaterial == null)
        {
            Debug.LogError("ResonanceController에 FlatKit FogSettings와 Effect Material이 필요합니다.", context);
            return false;
        }

        fogMaterial = fogSettings.effectMaterial;
        configuredDistanceIntensity = fogSettings.distanceFogIntensity;
        configuredHeightIntensity = fogSettings.heightFogIntensity;
        initialized = true;
        return true;
    }

    /// <summary>거리 근접도 + 제약 여부로 매 프레임 안개를 갱신한다.</summary>
    public void Tick(float proximity, bool inactive, float interpolation)
    {
        if (!initialized) return;

        float relief = maxConstraintRelief * proximity;
        float target = inactive ? 0f : activeIntensity * (1f - relief);
        Apply(Mathf.Lerp(currentIntensity, target, interpolation));
    }

    /// <summary>안개 강도를 특정 배율로 즉시 적용한다. (초기/스냅용)</summary>
    public void Apply(float multiplier)
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

    /// <summary>제약 상태 기준으로 안개 강도를 즉시 세팅한다.</summary>
    public void SnapConstrained(bool constrained) => Apply(constrained ? activeIntensity : 0f);

    /// <summary>스테이지별 안개 강도 오버라이드.</summary>
    public void SetActiveIntensity(float value) => activeIntensity = Mathf.Clamp01(value);

    /// <summary>재생 종료 시 원본 FogSettings/머티리얼 값을 복원한다.</summary>
    public void Restore()
    {
        if (!initialized || fogMaterial == null) return;

        fogSettings.distanceFogIntensity = configuredDistanceIntensity;
        fogSettings.heightFogIntensity = configuredHeightIntensity;
        fogMaterial.SetFloat(DistanceFogIntensityId, configuredDistanceIntensity);
        fogMaterial.SetFloat(HeightFogIntensityId, configuredHeightIntensity);
        
        initialized = false;
    }
}
