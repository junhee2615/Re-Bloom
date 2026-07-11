#ifndef HAZE_INCLUDED
#define HAZE_INCLUDED

#include "HazeCommon.hlsl"

TEXTURE3D(_ScatterBuffer);
float _VolumeNearClipPlane;
float _VolumeFarClipPlane;
float4x4 _FroxelVolumeVP;
TEXTURE2D(_GLOBAL_BloomTexture);
float _IGNStrength;
float _HazeBlurRadius;
float _HazeBloomIntensity;

float3 GetHazeUVW(float3 worldPos, float2 screenUV)
{
    uint bufferWidth;
    uint bufferHeight;
    uint bufferDepth;
    _ScatterBuffer.GetDimensions(bufferWidth, bufferHeight, bufferDepth);
    float3 uvw = WorldToUV(worldPos, _VolumeNearClipPlane, _VolumeFarClipPlane, lerp(_FroxelVolumeVP, UNITY_MATRIX_V, unity_OrthoParams.w), bufferDepth, unity_OrthoParams.w);   
    uvw.xyz += IGN(screenUV.x * _ScreenParams.x, screenUV.y * _ScreenParams.y, _Time.y * unity_DeltaTime.w) * 0.01 * _IGNStrength;
    return uvw;
}

float4 GetFinalHazeColor(float2 screenUV, float4 originalColor, float4 haze)
{
    float4 bloomTex = SAMPLE_TEXTURE2D(_GLOBAL_BloomTexture, hazeSampler_LinearClamp, screenUV);
    float4 color = lerp(originalColor, half4(bloomTex.rgb,1) * (1 / _HazeBlurRadius), _HazeBloomIntensity * (smoothstep(0.05, 0, haze.a)));
    color.rgb = color.rgb * lerp(haze.a, 1, _HazeBloomIntensity) + haze.rgb;
    color.a = haze.a;
    return color;
}

float4 SampleHaze(float3 worldPos, float2 screenUV)
{
    return SAMPLE_TEXTURE3D_LOD(_ScatterBuffer, hazeSampler_TrilinearClamp, GetHazeUVW(worldPos, screenUV), 0);
}

float4 SampleHazeTricubic(float3 worldPos, float2 screenUV)
{
    uint bufferWidth;
    uint bufferHeight;
    uint bufferDepth;
    _ScatterBuffer.GetDimensions(bufferWidth, bufferHeight, bufferDepth);
    return SampleTexture3DBicubic(_ScatterBuffer, GetHazeUVW(worldPos, screenUV), float3(bufferWidth, bufferHeight, bufferDepth));
}

void ApplyHaze(float3 worldPos, float2 screenUV, inout float4 color)
{
    float4 haze = SampleHaze(worldPos, screenUV);
    color.rgb = GetFinalHazeColor(screenUV, color, haze).rgb;
}

void ApplyHaze(float3 worldPos, float2 screenUV, inout float3 baseColor, inout float smoothness, inout float3 emission, inout float ambientOcclusion)
{
    float4 haze = SampleHaze(worldPos, screenUV);
    baseColor *= haze.a;
    smoothness = saturate(smoothness * haze.a);
    emission = GetFinalHazeColor(screenUV, float4(emission, 1), haze);
    ambientOcclusion = saturate(ambientOcclusion * haze.a);
}

void SampleHaze_float(float3 worldPos, float2 screenUV, out float3 hazeColor, out float hazeAlpha)
{
    float4 haze = SampleHaze(worldPos, screenUV);
    hazeColor = haze.rgb;
    hazeAlpha = haze.a;
}

void SampleHazeTricubic_float(float3 worldPos, float2 screenUV, out float3 hazeColor, out float hazeAlpha)
{
    float4 haze = SampleHazeTricubic(worldPos, screenUV);
    hazeColor = haze.rgb;
    hazeAlpha = haze.a;
}

void GetFinalHazeColor_float(float2 screenUV, float4 originalColor, float4 haze, out float4 finalColor)
{
    finalColor = GetFinalHazeColor(screenUV, originalColor, haze);
}

void SampleHaze_half(float3 worldPos, float2 screenUV, out float3 hazeColor, out float hazeAlpha)
{
    float4 haze = SampleHaze(worldPos, screenUV);
    hazeColor = haze.rgb;
    hazeAlpha = haze.a;
}

void SampleHazeTricubic_half(float3 worldPos, float2 screenUV, out float3 hazeColor, out float hazeAlpha)
{
    float4 haze = SampleHazeTricubic(worldPos, screenUV);
    hazeColor = haze.rgb;
    hazeAlpha = haze.a;
}

void GetFinalHazeColor_half(float2 screenUV, float4 originalColor, float4 haze, out float4 finalColor)
{
    finalColor = GetFinalHazeColor(screenUV, originalColor, haze);
}

#endif