// World Space UI 가 화면 안개에 덮이지 않게 "깊이만" 찍어 주는 셰이더.
//
// 왜 필요한가
//   FlatKitFog 는 ScriptableRendererFeature 이고 FogSettings.renderEvent 가 550
//   (BeforeRenderingPostProcessing) 이다. 반투명(500) 보다 뒤라서 World Space Canvas 가
//   그려진 뒤에 풀스크린으로 덮인다. 이때 안개 농도는 _CameraDepthTexture 로 정한다.
//   UI 셰이더(UI/Default, TMP SDF)는 ZWrite 가 꺼져 있어 깊이를 남기지 않으므로,
//   보드 픽셀의 깊이는 "보드 뒤 배경"의 깊이가 된다. FogSettings 가 near 0 / far 5 /
//   intensity 0.95 라, 2~3 m 앞의 보드가 수십 m 거리인 것처럼 최대 농도로 흐려진다.
//
// 무엇을 하는가
//   UI 와 같은 자리에 쿼드 하나를 불투명 큐에서 그리되 색은 쓰지 않고(ColorMask 0)
//   깊이만 남긴다. 그러면 안개 패스가 "여기 2.8 m 지점에 면이 있다"고 보고
//   그 거리에 맞는 옅은 안개만 입힌다.
//
//   Offset 2, 2 로 깊이를 아주 조금 뒤로 민다. UI 는 ZTest LEqual 이므로
//   쿼드와 완전히 같은 평면에 있어도 UI 가 먼저(앞) 판정돼 그대로 보인다.
//   Cull Off 라 보드를 어느 쪽에서 보든 동작한다.
//
//   UI 머티리얼·TMP·Mask·CanvasGroup 알파는 전혀 건드리지 않는다.
//   이 쿼드는 UI 가 아니라 별도 MeshRenderer 다.
//
// 세 패스를 두는 이유
//   URP 가 깊이 텍스처를 만드는 경로가 설정에 따라 다르다.
//     · 불투명 렌더 후 CopyDepth        → UniversalForward 패스의 ZWrite 가 쓰인다
//     · DepthPrepass                     → DepthOnly 패스가 쓰인다
//     · DepthNormalsPrepass (SSAO 등)    → DepthNormals 패스가 쓰인다
//   PC_Renderer 에는 ScreenSpaceAmbientOcclusion 이 있어 DepthNormals 경로가 쓰일 수
//   있고, Mobile_Renderer 는 그렇지 않다. 어느 쪽이든 깊이가 남도록 셋 다 넣는다.

Shader "ReBloom/World UI Depth Prime"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+100"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings DepthPrimeVertex(Attributes input)
        {
            Varyings output = (Varyings)0;

            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            return output;
        }
        ENDHLSL

        // 불투명 렌더 → CopyDepth 경로.
        Pass
        {
            Name "DepthPrime"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Off
            ColorMask 0
            Offset 2, 2

            HLSLPROGRAM
            #pragma vertex DepthPrimeVertex
            #pragma fragment DepthPrimeFragment
            #pragma multi_compile_instancing

            half4 DepthPrimeFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }

        // DepthPrepass 경로.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest LEqual
            Cull Off
            ColorMask 0
            Offset 2, 2

            HLSLPROGRAM
            #pragma vertex DepthPrimeVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }

        // DepthNormalsPrepass 경로(SSAO 등이 켜져 있을 때).
        // 법선은 평평한 정면값을 돌려준다. 이 쿼드는 눈에 보이지 않으므로 음영 품질과 무관하다.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            ZTest LEqual
            Cull Off
            Offset 2, 2

            HLSLPROGRAM
            #pragma vertex DepthPrimeVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(0.0h, 0.0h, 1.0h, 0.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
