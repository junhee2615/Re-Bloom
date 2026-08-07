using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives the Resonance post-processing Volume (weight + Bloom/Vignette) for the
/// constraint. Uses a runtime profile clone so the shared profile asset is preserved.
/// </summary>
[Serializable]
public sealed class VolumeConstraint
{
    [Tooltip("Resonance Global Volume.")]
    [SerializeField] private Volume resonanceVolume;
    [Tooltip("공명 해제 상태의 Volume weight.")]
    [SerializeField, Range(0f, 1f)] private float releasedVolumeWeight = 0.6f;
    [Tooltip("가까워져 제약이 완화됐을 때의 Volume weight.")]
    [SerializeField, Range(0f, 1f)] private float relievedVolumeWeight = 0.9f;

    // 완전 제약(멀리 떨어짐) 상태의 weight — 항상 최대.
    private const float constrainedVolumeWeight = 1f;

    private Bloom bloomOverride;
    private Vignette vignetteOverride;
    private float configuredBloomIntensity;
    private float configuredVignetteIntensity;
    private float smoothedVolumeWeight;
    private bool initialized;

    public bool Initialized => initialized;

    public void Initialize(bool constrained, UnityEngine.Object context)
    {
        if (initialized) return;

        if (resonanceVolume == null)
        {
            Debug.LogWarning("ResonanceController에 Resonance Global Volume이 지정되지 않았습니다. 제약 포스트 프로세싱 연출을 건너뜁니다.", context);
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

        float startWeight = constrained ? constrainedVolumeWeight : releasedVolumeWeight;
        resonanceVolume.weight = startWeight;
        smoothedVolumeWeight = startWeight;
        initialized = true;
    }

    /// <summary>
    /// Volume weight를 거리(proximity)로 구동한다.
    /// 멀리 = constrainedVolumeWeight(1), 가까움 = relievedVolumeWeight(0.9),
    /// 비활성/해제 = releasedVolumeWeight(0.6).
    /// </summary>
    public void UpdateWeight(float proximity, bool inactive, float interpolation)
    {
        if (!initialized) return;

        float targetWeight = inactive ? releasedVolumeWeight
            : Mathf.Lerp(constrainedVolumeWeight, relievedVolumeWeight, proximity);

        smoothedVolumeWeight = Mathf.Lerp(smoothedVolumeWeight, targetWeight, interpolation);
        resonanceVolume.weight = smoothedVolumeWeight;
    }

    /// <summary>
    /// 공명 on/off에 따라 Bloom/Vignette를 껐다 켠다.
    /// </summary>
    public void ApplyPostFx(bool inactive)
    {
        if (!initialized) return;

        float postFx = inactive ? 0f : 1f;
        if (bloomOverride != null)
            bloomOverride.intensity.value = configuredBloomIntensity * postFx;
        if (vignetteOverride != null)
            vignetteOverride.intensity.value = configuredVignetteIntensity * postFx;
    }

    /// <summary>테스트 토글 OFF 시 weight를 해제값으로 즉시 스냅한다.</summary>
    public void SnapReleased()
    {
        if (!initialized) return;

        smoothedVolumeWeight = releasedVolumeWeight;
        resonanceVolume.weight = releasedVolumeWeight;
    }
}
