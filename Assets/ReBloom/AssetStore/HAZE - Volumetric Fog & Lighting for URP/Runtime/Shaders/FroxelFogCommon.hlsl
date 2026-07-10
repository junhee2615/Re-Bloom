#ifndef FROXELFOGCOMMON_INCLUDED
#define FROXELFOGCOMMON_INCLUDED

#include "HazeCommon.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

//Lighting
real4 CustomSampleMainLightCookieTexture(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_MainLightCookieTexture, sampler_MainLightCookieTexture, uv, 0);
}

real3 CustomSampleMainLightCookie(float3 samplePositionWS)
{
    if(!IsMainLightCookieEnabled())
        return real3(1,1,1);

    float2 uv = ComputeLightCookieUVDirectional(_MainLightWorldToLight, samplePositionWS, float4(1, 1, 0, 0), URP_TEXTURE_WRAP_MODE_NONE);
    real4 color = CustomSampleMainLightCookieTexture(uv);

    return IsMainLightCookieTextureRGBFormat() ? color.rgb
             : IsMainLightCookieTextureAlphaFormat() ? color.aaa
             : color.rrr;
}

Light GetCustomMainLight(float3 positionWS)
{
    Light light = GetMainLight();

    #if defined(_LIGHT_COOKIES)
    real3 cookieColor = CustomSampleMainLightCookie(positionWS);
    light.color *= cookieColor;
    #endif

    return light;
}

Light CustomGetAdditionalPerObjectLight(int perObjectLightIndex, float3 positionWS)
{
    // Abstraction over Light input constants
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 lightPositionWS = _AdditionalLightsBuffer[perObjectLightIndex].position;
    half3 color = _AdditionalLightsBuffer[perObjectLightIndex].color.rgb;
    half4 distanceAndSpotAttenuation = _AdditionalLightsBuffer[perObjectLightIndex].attenuation;
    half4 spotDirection = _AdditionalLightsBuffer[perObjectLightIndex].spotDirection;
    uint lightLayerMask = _AdditionalLightsBuffer[perObjectLightIndex].layerMask;
    #else
    float4 lightPositionWS = _AdditionalLightsPosition[perObjectLightIndex];
    half3 color = _AdditionalLightsColor[perObjectLightIndex].rgb;
    half4 distanceAndSpotAttenuation = _AdditionalLightsAttenuation[perObjectLightIndex];
    half4 spotDirection = _AdditionalLightsSpotDir[perObjectLightIndex];
    uint lightLayerMask = asuint(_AdditionalLightsLayerMasks[perObjectLightIndex]);
    #endif

    // Directional lights store direction in lightPosition.xyz and have .w set to 0.0.
    // This way the following code will work for both directional and punctual lights.
    float3 lightVector = lightPositionWS.xyz - positionWS * lightPositionWS.w;
    float distanceSqr = max(dot(lightVector, lightVector), HALF_MIN);

    half3 lightDirection = half3(lightVector * rsqrt(distanceSqr));
    // full-float precision required on some platforms
    float attenuation = DistanceAttenuation(distanceSqr, distanceAndSpotAttenuation.xy) * AngleAttenuation(spotDirection.xyz, lightDirection, distanceAndSpotAttenuation.zw);
    //Spotlight razor
    // attenuation *= step(0.1, distanceSqr);

    Light light;
    light.direction = lightDirection;
    light.distanceAttenuation = attenuation;
    light.shadowAttenuation = 1.0; // This value can later be overridden in GetAdditionalLight(uint i, float3 positionWS, half4 shadowMask)
    light.color = color;
    light.layerMask = lightLayerMask;

    #if defined(_LIGHT_COOKIES)
    real3 cookieColor = SampleAdditionalLightCookie(perObjectLightIndex, positionWS);
    light.color *= cookieColor;
    #endif

    return light;
}


real CustomSampleShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, ShadowSamplingData samplingData, half4 shadowParams, bool isPerspectiveProjection = true)
{
    if (isPerspectiveProjection)
        shadowCoord.xyz /= shadowCoord.w;

    real shadowStrength = shadowParams.x;

#if SHADER_API_GLES3
    real attenuation = SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_LinearClamp, shadowCoord.xy, 0).x >= shadowCoord.z;
#else
    real attenuation = real(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz).x);
#endif
    attenuation = LerpWhiteTo(attenuation, shadowStrength);
    return attenuation;
}

half CustomAdditionalLightRealtimeShadow(int lightIndex, float3 positionWS, half3 lightDirection, half4 shadowParams, ShadowSamplingData shadowSamplingData)
{
    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
    int shadowSliceIndex = shadowParams.w;
    if (shadowSliceIndex < 0)
        return 1.0;

    half isPointLight = shadowParams.z;

    UNITY_BRANCH
    if (isPointLight)
    {
        // This is a point light, we have to find out which shadow slice to sample from
        const int cubeFaceOffset = CubeMapFaceID(-lightDirection);
        shadowSliceIndex += cubeFaceOffset;
    }

    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 shadowCoord = mul(_AdditionalLightsWorldToShadow_SSBO[shadowSliceIndex], float4(positionWS, 1.0));
    #else
    float4 shadowCoord = mul(_AdditionalLightsWorldToShadow[shadowSliceIndex], float4(positionWS, 1.0));
    #endif

    return CustomSampleShadowmap(TEXTURE2D_ARGS(_AdditionalLightsShadowmapTexture, sampler_LinearClampCompare), shadowCoord, shadowSamplingData, shadowParams, true);
    #else
    return half(1.0);
    #endif
}

half CustomAdditionalLightRealtimeShadow(int lightIndex, float3 positionWS, half3 lightDirection)
{
    #if !defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
    return half(1.0);
    #endif

    return CustomAdditionalLightRealtimeShadow(lightIndex, positionWS, lightDirection, GetAdditionalLightShadowParams(lightIndex), GetAdditionalLightShadowSamplingData(lightIndex));
}

float4 CustomTransformWorldToShadowCoord(float3 positionWS)
{
    #if defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    half cascadeIndex = ComputeCascadeIndex(positionWS);
    #else
    half cascadeIndex = half(0.0);
    #endif

    float4 shadowCoord = float4(mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0)).xyz, 0.0);
    return shadowCoord;
}

float LightScattering(float3 Wo, float3 Wi, float g)
{
    g = 1.0 - g;
    float cos_theta = dot(Wo, Wi);
    float denom     = 1.0f + g * g + 2.0f * g * cos_theta;
    return (1.0f / (4.0f * PI)) * (1.0f - g * g) / max(pow(abs(denom), 1.5f), FLT_EPS);
}

#endif