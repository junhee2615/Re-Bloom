Shader "Hidden/Haze/Composite"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZWrite Off Cull Off
        Pass
        {
            Name "HazeComposite"

            HLSLPROGRAM
            #include "FroxelFogCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment frag

            #pragma multi_compile_local_fragment _ TRICUBIC_SAMPLING TRILINEAR_SAMPLING POINT_SAMPLING

            TEXTURE3D(_ScatterBuffer);
            TEXTURE2D(_GLOBAL_BloomTexture);
            real _HazeBloomIntensity;
            real _VolumeNearClipPlane;
            real _VolumeFarClipPlane;
            real4x4 _FroxelVolumeVP;
            real _HazeBlurRadius;
            real _IGNStrength;
            real _HazeDepthBias;

            half4 frag (Varyings input) : SV_Target
            {
                uint bufferWidth;
                uint bufferHeight;
                uint bufferDepth;
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
#if UNITY_REVERSED_Z
                real depth = SampleSceneDepth(input.texcoord);
#else
                real depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(input.texcoord));
#endif
                real3 worldPos = ComputeWorldSpacePosition(input.texcoord, depth, UNITY_MATRIX_I_VP);
                _ScatterBuffer.GetDimensions(bufferWidth, bufferHeight, bufferDepth);
                real3 uvw = WorldToUV(worldPos, _VolumeNearClipPlane, _VolumeFarClipPlane, lerp(_FroxelVolumeVP, UNITY_MATRIX_V, unity_OrthoParams.w), bufferDepth, unity_OrthoParams.w);
                uvw.z -= _HazeDepthBias;
                uvw.xyz += IGN(input.texcoord.x * _BlitTexture_TexelSize.z, input.texcoord.y * _BlitTexture_TexelSize.w, _Time.y * unity_DeltaTime.w) * 0.01 * _IGNStrength;
#ifdef TRICUBIC_SAMPLING
                real4 scatterBuffer = SampleTexture3DBicubic(_ScatterBuffer, uvw, real3(bufferWidth, bufferHeight, bufferDepth));
#elif POINT_SAMPLING
                real4 scatterBuffer = SAMPLE_TEXTURE3D_LOD(_ScatterBuffer, sampler_PointClamp, uvw, 0);
#else
                real4 scatterBuffer = SAMPLE_TEXTURE3D_LOD(_ScatterBuffer, sampler_TrilinearClamp, uvw, 0);
#endif
                real4 bloomTex = SAMPLE_TEXTURE2D(_GLOBAL_BloomTexture, sampler_LinearClamp, input.texcoord);
                real4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                color = lerp(color, half4(bloomTex.rgb,1) * (1 / _HazeBlurRadius), _HazeBloomIntensity * (1.0 - scatterBuffer.a));
                color.rgb = color.rgb * lerp(scatterBuffer.a, 1, _HazeBloomIntensity) + scatterBuffer.rgb;
                return real4(color.rgb, 1);
            }
            ENDHLSL
        }
    }
}