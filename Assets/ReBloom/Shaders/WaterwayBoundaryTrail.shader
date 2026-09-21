// LineRenderer용 초경량 URP Unlit Transparent 셰이더.
//
// - 정점 색(COLOR)을 그대로 읽어 RGB·Alpha 모두 최종 출력에 곱한다.
//   → LineRenderer.colorGradient(꼬리 알파 0 → 머리 알파 1)가 실제 투명도로 반영된다.
// - rgb = (BaseColor.rgb + EmissionColor.rgb) × VertexColor.rgb,  a = BaseColor.a × VertexColor.a
// - 조명·노멀·텍스처 없음. XR Single Pass Instanced 스테레오 매크로 포함. SRP Batcher 호환.
Shader "ReBloom/Waterway Boundary Trail"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0, 0.8, 0.65, 0.55)
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 1.6, 1.3, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WaterwayBoundaryTrail"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 rgb = (_BaseColor.rgb + _EmissionColor.rgb) * input.color.rgb;
                half  a   = _BaseColor.a * input.color.a;
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
