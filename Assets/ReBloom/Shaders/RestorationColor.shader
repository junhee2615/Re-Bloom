Shader "Hidden/ReBloom/RestorationColor"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Saturation;
            float _PostExposure;
            float _Contrast;
            float4 _ColorFilter;
            float _FilterStrength;
            float _DebugSolidColor; // TEMP diagnostic: 원인 확인 후 제거

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // TEMP diagnostic: 셰이더 출력이 화면에 반영되는지 확인용 단색 출력
                if (_DebugSolidColor > 0.5)
                    return half4(1.0h, 0.0h, 0.0h, 1.0h);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                half3 corrected = source.rgb * exp2(_PostExposure);

                corrected = (corrected - 0.18h) * (1.0h + _Contrast * 0.01h) + 0.18h;

                half luminance = dot(corrected, half3(0.2126h, 0.7152h, 0.0722h));
                corrected = lerp(luminance.xxx, corrected, 1.0h + _Saturation * 0.01h);
                corrected *= _ColorFilter.rgb;

                return half4(lerp(source.rgb, corrected, saturate(_FilterStrength)), source.a);
            }
            ENDHLSL
        }
    }
}
