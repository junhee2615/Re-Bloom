// ReBloom / Riverbed Wetness
// 마른 강바닥이 상류에서부터 "젖어들어오는" 표현.
// 스캐너 링이 아니라 물이 스며드는 문법. URP + VR(Single Pass Instanced) 대응.
//
// 런타임 제어: C#에서 Shader.SetGlobal* 로 아래 전역값을 넣는다.
//   Shader.SetGlobalVector("_WetOrigin",  new Vector4(x, y, z, 0));  // 상류 원점(월드)
//   Shader.SetGlobalVector("_WetFlowDir", new Vector4(0, 0, 1, 0));  // 하류 방향
//   Shader.SetGlobalFloat ("_WetFront",   front);                    // 원점에서 진행한 거리(m)
//   Shader.SetGlobalFloat ("_WetAmount",  amount);                   // 0~1 젖음 총량(마름 = 0으로 감소)
//   Shader.SetGlobalFloat ("_CrestGain",  gain);                     // 0~1 물마루 표시(통과 중 1)
// 이때 머티리얼의 Preview Mode 를 0 으로 꺼야 전역값이 적용된다.

Shader "ReBloom/Riverbed Wetness"
{
    Properties
    {
        [Header(Dry Surface)]
        _BaseMap        ("Base Map", 2D) = "white" {}
        _BaseColor      ("Base Color", Color) = (1,1,1,1)
        _BumpMap        ("Normal Map", 2D) = "bump" {}
        _BumpScale      ("Normal Scale", Range(0,2)) = 1.0
        _Smoothness     ("Dry Smoothness", Range(0,1)) = 0.05
        _Metallic       ("Metallic", Range(0,1)) = 0.0

        [Header(Wet Look)]
        _WetTint        ("Wet Tint", Color) = (0.62,0.70,0.74,1)
        _WetDarken      ("Wet Darken", Range(0,1)) = 0.55
        _WetSmoothness  ("Wet Smoothness", Range(0,1)) = 0.85
        _NormalFlatten  ("Wet Normal Flatten", Range(0,1)) = 0.55

        [Header(Front Shape)]
        _EdgeWidth      ("Edge Softness (m)", Range(0.01,3)) = 0.45
        _GrainScale     ("Grain Scale", Range(0.02,3)) = 0.35
        _GrainStrength  ("Grain Strength (m)", Range(0,8)) = 1.6
        _PoolStrength   ("Pooling By Height", Range(0,4)) = 1.2
        _PoolLead       ("Pooling Lead (m)", Range(0,4)) = 0.8

        [Header(Crest)]
        _CrestColor     ("Crest Color", Color) = (0.75,0.90,1.00,1)
        _CrestWidth     ("Crest Width (m)", Range(0.05,4)) = 0.90
        _CrestIntensity ("Crest Intensity", Range(0,4)) = 1.0

        [Header(Ripple)]
        _RippleStrength ("Ripple Strength", Range(0,1)) = 0.25
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
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 300

        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                float4 _BaseColor;
                float4 _WetTint;
                float4 _CrestColor;
                float4 _PreviewOrigin;
                float4 _PreviewFlowDir;
                float  _BumpScale;
                float  _Smoothness;
                float  _Metallic;
                float  _WetDarken;
                float  _WetSmoothness;
                float  _NormalFlatten;
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

            // --- C# 에서 Shader.SetGlobal* 로 넣는 값. Properties 에 두면 안 된다
            //     (머티리얼 값이 전역값을 덮어써서 영영 안 먹는다) ---
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

            struct WetSample
            {
                float wet;    // 0~1 젖음
                float crest;  // 물마루 밝기
                float grain;  // 결 노이즈 (재사용용)
            };

            WetSample EvaluateWetness(float3 positionWS)
            {
                // 프리뷰 모드면 머티리얼 값을, 아니면 전역값을 쓴다
                float  m       = saturate(_PreviewMode);
                float3 origin  = lerp(_WetOrigin.xyz,  _PreviewOrigin.xyz,  m);
                float3 rawFlow = lerp(_WetFlowDir.xyz, _PreviewFlowDir.xyz, m);
                float  front   = lerp(_WetFront,       _PreviewFront,       m);
                float  amount  = lerp(_WetAmount,      _PreviewAmount,      m);
                float  crestG  = lerp(_CrestGain,      _PreviewCrest,       m);

                float flowLen = length(rawFlow);
                float3 flow   = flowLen > 1e-4 ? rawFlow / flowLen : float3(0.0, 0.0, 1.0);

                // 원점에서 하류 방향으로 몇 m 떨어진 지점인가
                float axial = dot(positionWS - origin, flow);

                // 결(grain): 전선을 직선이 아니라 들쭉날쭉하게 만든다
                float g1 = ValueNoise(positionWS.xz * _GrainScale);
                float g2 = ValueNoise(positionWS.xz * _GrainScale * 2.7 + 17.3);
                float grain = saturate(g1 * 0.65 + g2 * 0.35);

                // 고임(pooling): 원점보다 낮은 곳은 먼저 젖고 더 오래 젖어 있다
                float drop = max(0.0, origin.y - positionWS.y);
                float pool = saturate(drop * _PoolStrength);

                float localFront = front + (grain - 0.5) * _GrainStrength + pool * _PoolLead;

                float edge = max(_EdgeWidth, 0.001);
                // axial 이 전선보다 작으면(=이미 지나갔으면) 1
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
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv         : TEXCOORD0;   // xy = base, zw = bump
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
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
                VertexNormalInputs   vn = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = vp.positionCS;
                output.positionWS = vp.positionWS;
                output.normalWS   = vn.normalWS;
                output.tangentWS  = half4(vn.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv.xy      = TRANSFORM_TEX(input.uv, _BaseMap);
                output.uv.zw      = TRANSFORM_TEX(input.uv, _BumpMap);
                output.fogCoord   = ComputeFogFactor(vp.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                WetSample w = EvaluateWetness(input.positionWS);

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy).rgb * _BaseColor.rgb;
                half3 wetAlbedo = albedo * _WetDarken * _WetTint.rgb;
                albedo = lerp(albedo, wetAlbedo, w.wet);

                // 노멀: 젖으면 미세요철이 물에 메워져 평탄해진다
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv.zw), _BumpScale);
                nTS.xy *= lerp(1.0, 1.0 - _NormalFlatten, w.wet);
                nTS = normalize(nTS);

                float  sgn       = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                half3x3 tbn      = half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz);
                float3 normalWS  = normalize(TransformTangentToWorld(nTS, tbn));

                // 수면 잔물결은 월드 XZ 기준으로 얹는다(강바닥은 대체로 수평)
                if (_RippleStrength > 0.001)
                {
                    float2 grad = RippleGradient(input.positionWS, _Time.y);
                    float  amp  = _RippleStrength * 6.0 * saturate(w.wet + w.crest * 0.5);
                    normalWS = normalize(normalWS + float3(grad.x, 0.0, grad.y) * amp);
                }

                SurfaceData surface = (SurfaceData)0;
                surface.albedo             = albedo;
                surface.metallic           = _Metallic;
                surface.specular           = half3(0.0, 0.0, 0.0);
                surface.smoothness         = lerp(_Smoothness, _WetSmoothness, w.wet);
                surface.normalTS           = half3(0.0, 0.0, 1.0);
                surface.occlusion          = 1.0;
                surface.emission           = _CrestColor.rgb * w.crest;
                surface.alpha              = 1.0;
                surface.clearCoatMask      = 0.0;
                surface.clearCoatSmoothness = 0.0;

                InputData inputData = (InputData)0;
                inputData.positionWS              = input.positionWS;
                inputData.normalWS                = normalWS;
                inputData.viewDirectionWS         = normalize(GetWorldSpaceViewDir(input.positionWS));
                inputData.shadowCoord             = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord                = input.fogCoord;
                inputData.vertexLighting          = half3(0.0, 0.0, 0.0);
                inputData.bakedGI                 = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask              = half4(1.0, 1.0, 1.0, 1.0);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, input.fogCoord);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // SRP Batcher 호환을 위해 모든 패스가 동일한 CBUFFER 레이아웃을 가져야 한다
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                float4 _BaseColor;
                float4 _WetTint;
                float4 _CrestColor;
                float4 _PreviewOrigin;
                float4 _PreviewFlowDir;
                float  _BumpScale;
                float  _Smoothness;
                float  _Metallic;
                float  _WetDarken;
                float  _WetSmoothness;
                float  _NormalFlatten;
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

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BumpMap_ST;
                float4 _BaseColor;
                float4 _WetTint;
                float4 _CrestColor;
                float4 _PreviewOrigin;
                float4 _PreviewFlowDir;
                float  _BumpScale;
                float  _Smoothness;
                float  _Metallic;
                float  _WetDarken;
                float  _WetSmoothness;
                float  _NormalFlatten;
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

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
