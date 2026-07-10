Shader "Hidden/Haze/Bloom"
{
    HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DynamicScalingClamping.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
        #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"
        #include "FroxelFogCommon.hlsl"

        TEXTURE2D_X(_SourceTexLowMip);
        float4 _SourceTexLowMip_TexelSize;

        float4 _Params; // x: scatter, y: clamp, z: threshold (linear), w: threshold knee
        float _SampleScale;
        
        TEXTURE3D(_ScatterBuffer);
        float _VolumeNearClipPlane;
        float _VolumeFarClipPlane;
        float4x4 _FroxelVolumeVP;
        float _Intensity;
        float _Radius;

        #define Scatter             _Params.x
        #define ClampMax            _Params.y
        #define Threshold           _Params.z
        #define ThresholdKnee       _Params.w

        half4 EncodeHDR(half3 color)
        {
        #if UNITY_COLORSPACE_GAMMA
            color = sqrt(color); // linear to γ
        #endif

            return half4(color, 1.0);
        }

        half3 DecodeHDR(half4 data)
        {
            half3 color = data.xyz;

        #if UNITY_COLORSPACE_GAMMA
            color *= color; // γ to linear
        #endif

            return color;
        }

        half Brightness(half3 c)
        {
            return max(max(c.r, c.g), c.b);
        }

        half3 Median(half3 a, half3 b, half3 c)
        {
            return a + b + c - min(min(a, b), c) - max(max(a, b), c);
        }

        half4 Median(half4 a, half4 b, half4 c)
        {
            return a + b + c - min(min(a, b), c) - max(max(a, b), c);
        }

        half3 DownsampleFilter(float2 uv)
        {
             float4 d = _BlitTexture_TexelSize.xyxy * float4(-1, -1, +1, +1);

            half3 s;
            s  = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.xy));
            s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.zy));
            s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.xw));
            s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.zw));

            return s * (1.0 / 4);
        }

        half3 UpsampleFilter(float2 uv)
        {
             float4 d = _BlitTexture_TexelSize.xyxy * float4(-1, -1, +1, +1) * (_SampleScale * 0.5);

            half3 s;
            s  = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.xy));
            s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.zy));
            s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.xw));
            s += DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + d.zw));

            return s * (1.0 / 4);
        }

        half3 SamplePrefilter(float2 uv,  float2 offset)
        {
            float2 texelSize = _BlitTexture_TexelSize.xy;
            half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texelSize * offset);
            #if _ENABLE_ALPHA_OUTPUT
                // When alpha is enabled, regions with zero alpha should not generate any bloom / glow. Therefore we pre-multipy the color with the alpha channel here and the rest
                // of the computations remain float3. Still, when bloom is applied to the final image, bloom will still be spread on regions with zero alpha (see UberPost.compute)
                color.xyz *= color.w;
            #endif
            return color.xyz;
        }

        half4 FragPrefilter(Varyings input) : SV_Target
        {
            float2 uv = input.texcoord + _BlitTexture_TexelSize.xy * 0;
            float3 d = _BlitTexture_TexelSize.xyx * float3(1, 1, 0);
            float3 s0 = SamplePrefilter(uv, 0).rgb;
            float3 s1 = SamplePrefilter(uv, -d.xz).rgb;
            float3 s2 = SamplePrefilter(uv, d.xz).rgb;
            float3 s3 = SamplePrefilter(uv, -d.zy).rgb;
            float3 s4 = SamplePrefilter(uv, d.zy).rgb;
            float3 m = Median(Median(s0, s1, s2), s3, s4);

            float3 curve = float3(Threshold - ThresholdKnee, ThresholdKnee * 2, 0.25 / ThresholdKnee);
            float br = Brightness(m);
            half rq = clamp(br - curve.x, 0, curve.y);
            rq = curve.z * rq * rq;

            // Combine and apply the brightness response curve.
            float multiplier = max(rq, br - Threshold) / max(br, 1e-5); 
            m *= multiplier;

            return EncodeHDR(m);
        }

        half4 FragBlurH(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 texelSize = _BlitTexture_TexelSize.xy * 2.0;
            float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

            // 9-tap gaussian blur on the downsampled source
            half3 c0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 4.0, 0.0), texelSize)));
            half3 c1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 3.0, 0.0), texelSize)));
            half3 c2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 2.0, 0.0), texelSize)));
            half3 c3 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(texelSize.x * 1.0, 0.0), texelSize)));
            half3 c4 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv                                 , texelSize)));
            half3 c5 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 1.0, 0.0), texelSize)));
            half3 c6 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 2.0, 0.0), texelSize)));
            half3 c7 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 3.0, 0.0), texelSize)));
            half3 c8 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(texelSize.x * 4.0, 0.0), texelSize)));

            half3 color = c0 * 0.01621622 + c1 * 0.05405405 + c2 * 0.12162162 + c3 * 0.19459459
                        + c4 * 0.22702703
                        + c5 * 0.19459459 + c6 * 0.12162162 + c7 * 0.05405405 + c8 * 0.01621622;

            return EncodeHDR(color);
        }

        half4 FragBlurV(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 texelSize = _BlitTexture_TexelSize.xy;
            float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

            // Optimized bilinear 5-tap gaussian on the same-sized source (9-tap equivalent)
            half3 c0 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(0.0, texelSize.y * 3.23076923), texelSize)));
            half3 c1 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv - float2(0.0, texelSize.y * 1.38461538), texelSize)));
            half3 c2 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv                                        , texelSize)));
            half3 c3 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(0.0, texelSize.y * 1.38461538), texelSize)));
            half3 c4 = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, ClampUVForBilinear(uv + float2(0.0, texelSize.y * 3.23076923), texelSize)));

            half3 color = c0 * 0.07027027 + c1 * 0.31621622
                        + c2 * 0.22702703
                        + c3 * 0.31621622 + c4 * 0.07027027;

            return EncodeHDR(color);
        }

        half3 Upsample(float2 uv, float fogDensity)
        {
            half3 highMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv));

        #if _BLOOM_HQ
            half3 lowMip = DecodeHDR(SampleTexture2DBicubic(TEXTURE2D_X_ARGS(_SourceTexLowMip, sampler_LinearClamp), uv, _SourceTexLowMip_TexelSize.zwxy, (1.0).xx, unity_StereoEyeIndex));
        #else
            half3 lowMip = DecodeHDR(SAMPLE_TEXTURE2D_X(_SourceTexLowMip, sampler_LinearClamp, uv));
        #endif

            return lerp(highMip, lowMip, Scatter * fogDensity);
        }

        half4 FragUpsample(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            uint bufferWidth;
            uint bufferHeight;
            uint bufferDepth;
            _ScatterBuffer.GetDimensions(bufferWidth, bufferHeight, bufferDepth);
#if UNITY_REVERSED_Z
            real depth = SampleSceneDepth(input.texcoord);
#else
            real depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(input.texcoord));
#endif
            float3 worldPos = ComputeWorldSpacePosition(input.texcoord, depth, UNITY_MATRIX_I_VP);
            _ScatterBuffer.GetDimensions(bufferWidth, bufferHeight, bufferDepth);
            float3 uvw = WorldToUV(worldPos, _VolumeNearClipPlane, _VolumeFarClipPlane, lerp(_FroxelVolumeVP, UNITY_MATRIX_V, unity_OrthoParams.w), bufferDepth, unity_OrthoParams.w);
            
            float4 fog = SAMPLE_TEXTURE3D(_ScatterBuffer, sampler_TrilinearClamp, uvw);
            half3 color = Upsample(UnityStereoTransformScreenSpaceTex(input.texcoord), saturate((1.0 - fog.a)));
            return EncodeHDR(color);
        }


    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Bloom Prefilter"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragPrefilter
                #pragma multi_compile_local_fragment _ _BLOOM_HQ
                #pragma multi_compile_fragment _ _ENABLE_ALPHA_OUTPUT
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Horizontal"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBlurH
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Blur Vertical"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBlurV
            ENDHLSL
        }

        Pass
        {
            Name "Bloom Upsample"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragUpsample
                #pragma multi_compile_local_fragment _ _BLOOM_HQ
            ENDHLSL
        }
    }
}
