// ReBloom / Riverbed Wetness  (transparent overlay)
//
// 메시 자체는 보이지 않는다. Terrain 위에 얹어두면 "젖은 부분"만 나타나서
// 물결이 지형을 타고 흐르는 것처럼 보인다.
//
// 블렌딩은 프리멀티플라이드 알파(Blend One OneMinusSrcAlpha)를 쓴다.
//   결과 = src.rgb + dst.rgb * (1 - src.a)
//   - alpha 를 올리면 아래(Terrain)가 어두워진다  -> 젖음
//   - alpha 없이 rgb 만 더하면 밝아진다           -> 반사광 / 물마루
// 덕분에 한 패스로 "어두워짐 + 윤기"를 동시에 만들고, 마른 곳은 alpha=0, rgb=0 이라
// Terrain 이 그대로 보인다.
//
// 런타임 제어(전역):
//   _WetOrigin / _WetFlowDir / _WetFront / _WetAmount / _CrestGain
// 머티리얼의 Preview Mode 를 켜면 인스펙터 값으로 미리 볼 수 있다.

Shader "ReBloom/Riverbed Wetness"
{
    Properties
    {
        [Header(Wet Look)]
        _WetTint        ("Wet Tint", Color) = (0.34,0.40,0.44,1)
        _WetDarken      ("Wet Darken", Range(0,1)) = 0.55
        _WetOpacity     ("Wet Coverage", Range(0,1)) = 0.75

        [Header(Sheen)]
        _SpecIntensity  ("Sun Sheen", Range(0,8)) = 2.2
        _SpecPower      ("Sun Sheen Tightness", Range(4,256)) = 64
        _EnvIntensity   ("Sky Reflection", Range(0,3)) = 0.85
        _FresnelPower   ("Fresnel Power", Range(1,8)) = 4

        [Header(Front Shape)]
        _EdgeWidth      ("Edge Softness (m)", Range(0.01,3)) = 0.45
        _GrainScale     ("Grain Scale", Range(0.02,3)) = 0.35
        _GrainStrength  ("Grain Strength (m)", Range(0,8)) = 2.2
        _PoolStrength   ("Pooling By Height", Range(0,4)) = 1.2
        _PoolLead       ("Pooling Lead (m)", Range(0,4)) = 0.8

        [Header(Crest)]
        _CrestColor     ("Crest Color", Color) = (0.70,0.88,1.00,1)
        _CrestWidth     ("Crest Width (m)", Range(0.05,4)) = 1.1
        _CrestIntensity ("Crest Intensity", Range(0,4)) = 1.2

        [Header(Ripple)]
        _RippleStrength ("Ripple Strength", Range(0,1)) = 0.35
        _RippleScale    ("Ripple Scale", Range(0.5,20)) = 6.0
        _RippleSpeed    ("Ripple Speed", Range(0,4)) = 0.8

        [Header(Editor Preview   set Mode to 0 at runtime)]
        [ToggleUI] _PreviewMode ("Preview Mode", Float) = 1
        _PreviewOrigin  ("Preview Origin (WS)", Vector) = (0,0,0,0)
        _PreviewFlowDir ("Preview Flow Dir", Vector) = (0,0,1,0)
        _PreviewFront   ("Preview Front (m)", Float) = 5.0
        _PreviewAmount  ("Preview Amount", Range(0,1)) = 1.0
        _PreviewCrest   ("Preview Crest", Range(0,1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent-100"
            "IgnoreProjector" = "True"
        }
        LOD 200

        Pass
        {
            Name "WetOverlay"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha   // premultiplied alpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WetTint;
                float4 _CrestColor;
                float4 _PreviewOrigin;
                float4 _PreviewFlowDir;
                float  _WetDarken;
                float  _WetOpacity;
                float  _SpecIntensity;
                float  _SpecPower;
                float  _EnvIntensity;
                float  _FresnelPower;
                float  _EdgeWidth;
                float  _GrainScale;
                float  _GrainStrength;
                float  _PoolStrength;
                float  _PoolLead;
                float  _CrestWidth;
                float  _CrestIntensity;
                float  _RippleStrength;
                float  _RippleScale;
                float  _RippleSpeed;
                float  _PreviewMode;
                float  _PreviewFront;
                float  _PreviewAmount;
                float  _PreviewCrest;
            CBUFFER_END

            // --- C# 에서 Shader.SetGlobal* 로 넣는 값 (Properties 에 두면 안 된다) ---
            float4 _WetOrigin;
            float4 _WetFlowDir;
            float  _WetFront;
            float  _WetAmount;
            float  _CrestGain;

            // ------------------------------------------------------------
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            struct WetSample { float wet; float crest; float grain; };

            WetSample EvaluateWetness(float3 positionWS)
            {
                float  m       = saturate(_PreviewMode);
                float3 origin  = lerp(_WetOrigin.xyz,  _PreviewOrigin.xyz,  m);
                float3 rawFlow = lerp(_WetFlowDir.xyz, _PreviewFlowDir.xyz, m);
                float  front   = lerp(_WetFront,       _PreviewFront,       m);
                float  amount  = lerp(_WetAmount,      _PreviewAmount,      m);
                float  crestG  = lerp(_CrestGain,      _PreviewCrest,       m);

                float flowLen = length(rawFlow);
                float3 flow   = flowLen > 1e-4 ? rawFlow / flowLen : float3(0.0, 0.0, 1.0);

                float axial = dot(positionWS - origin, flow);

                float g1 = ValueNoise(positionWS.xz * _GrainScale);
                float g2 = ValueNoise(positionWS.xz * _GrainScale * 2.7 + 17.3);
                float grain = saturate(g1 * 0.65 + g2 * 0.35);

                float drop = max(0.0, origin.y - positionWS.y);
                float pool = saturate(drop * _PoolStrength);

                float localFront = front + (grain - 0.5) * _GrainStrength + pool * _PoolLead;

                float edge = max(_EdgeWidth, 0.001);
                float arrive = 1.0 - smoothstep(localFront - edge, localFront + edge, axial);

                WetSample s;
                s.grain = grain;
                s.wet   = saturate(arrive * amount * (0.75 + 0.25 * grain + 0.35 * pool));

                float band = 1.0 - saturate(abs(axial - localFront) / max(_CrestWidth, 0.001));
                s.crest = band * band * _CrestIntensity * crestG * saturate(amount * 3.0);
                return s;
            }

            float2 RippleGradient(float3 positionWS, float t)
            {
                float2 uv = positionWS.xz * _RippleScale + float2(0.0, t * _RippleSpeed);
                const float e = 0.35;
                float n  = ValueNoise(uv);
                float nx = ValueNoise(uv + float2(e, 0.0));
                float ny = ValueNoise(uv + float2(0.0, e));
                return float2(nx - n, ny - n);
            }

            // ------------------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs vp = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   vn = GetVertexNormalInputs(input.normalOS);

                output.positionCS = vp.positionCS;
                output.positionWS = vp.positionWS;
                output.normalWS   = vn.normalWS;
                output.fogCoord   = ComputeFogFactor(vp.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                WetSample w = EvaluateWetness(input.positionWS);

                // 완전히 마른 곳은 아무것도 그리지 않는다 (Terrain 이 그대로 보인다)
                float visible = saturate(w.wet + w.crest);
                clip(visible - 0.002);

                float3 normalWS = normalize(input.normalWS);
                if (_RippleStrength > 0.001)
                {
                    float2 grad = RippleGradient(input.positionWS, _Time.y);
                    normalWS = normalize(normalWS + float3(grad.x, 0.0, grad.y)
                        * (_RippleStrength * 6.0 * saturate(w.wet + w.crest * 0.5)));
                }

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 1) 젖음 = 아래를 어둡게 (알파로 표현)
                float coverage = saturate(w.wet * _WetOpacity);
                half3 wetRGB = _WetTint.rgb * _WetDarken * coverage;   // premultiplied

                // 2) 햇빛 반사 (물이 반짝이는 부분)
                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float spec = pow(saturate(dot(normalWS, halfDir)), _SpecPower);
                half3 sheen = mainLight.color * (spec * _SpecIntensity * w.wet);

                // 3) 하늘 반사 — 스치는 각도에서 강해진다 (프레넬)
                float fres = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _FresnelPower);
                half3 sky = SampleSH(reflect(-viewDirWS, normalWS));
                half3 envRGB = sky * (fres * _EnvIntensity * w.wet);

                // 4) 물마루
                half3 crestRGB = _CrestColor.rgb * w.crest;

                half3 rgb = wetRGB + sheen + envRGB + crestRGB;
                half  a   = coverage;

                // 안개는 더해지는 성분에만 적용 (프리멀티플라이드라 알파는 건드리지 않는다)
                rgb = MixFog(rgb, input.fogCoord);

                return half4(rgb, a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
