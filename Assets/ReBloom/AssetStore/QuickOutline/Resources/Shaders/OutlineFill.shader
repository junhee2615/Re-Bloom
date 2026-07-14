////  OutlineFill.shader
//  QuickOutline
////  Created by Chris Nolet on 2/21/18.
//  Copyright © 2018 Chris Nolet. All rights reserved.
//

Shader "Custom/Outline Fill" {
  Properties {
    [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0

    _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
    _OutlineWidth("Outline Width", Range(0, 10)) = 2

    // --- 🌿 추가된 부분: 텍스처와 투명도 기준값 ---
    _MainTex("Main Texture", 2D) = "white" {}
    _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
  }

  SubShader {
    Tags {
      "Queue" = "Transparent+110"
      "RenderType" = "TransparentCutout" // 변경: 투명도를 인식하도록 수정
      "DisableBatching" = "True"
    }

    Pass {
      Name "Fill"
      Cull Off
      ZTest [_ZTest]
      ZWrite Off
      Blend SrcAlpha OneMinusSrcAlpha
      ColorMask RGB

      Stencil {
        Ref 1
        Comp NotEqual
      }

      CGPROGRAM
      #include "UnityCG.cginc"
      #pragma vertex vert
      #pragma fragment frag

      struct appdata {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
        float3 smoothNormal : TEXCOORD3;
        float2 uv : TEXCOORD0; // --- 🌿 추가: 텍스처 좌표(UV) 받아오기 ---
        UNITY_VERTEX_INPUT_INSTANCE_ID
      };

      struct v2f {
        float4 position : SV_POSITION;
        fixed4 color : COLOR;
        float2 uv : TEXCOORD0; // --- 🌿 추가: 텍스처 좌표(UV) 전달용 ---
        UNITY_VERTEX_OUTPUT_STEREO
      };

      uniform fixed4 _OutlineColor;
      uniform float _OutlineWidth;
      
      // --- 🌿 추가: 텍스처와 컷오프 변수 선언 ---
      sampler2D _MainTex;
      float4 _MainTex_ST;
      float _Cutoff;

      v2f vert(appdata input) {
        v2f output;

        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        float3 normal = any(input.smoothNormal) ? input.smoothNormal : input.normal;
        float3 viewPosition = UnityObjectToViewPos(input.vertex);
        float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, normal));

        output.position = UnityViewToClipPos(viewPosition + viewNormal * -viewPosition.z * _OutlineWidth / 1000.0);
        output.color = _OutlineColor;
        
        // --- 🌿 추가: 텍스처 UV 맵핑 ---
        output.uv = TRANSFORM_TEX(input.uv, _MainTex);

        return output;
      }

      fixed4 frag(v2f input) : SV_Target {
        // --- 🌿 추가: 텍스처 투명도 인식 및 잘라내기 ---
        fixed4 texColor = tex2D(_MainTex, input.uv);
        
        // 픽셀의 투명도(a)가 _Cutoff 값보다 낮으면 렌더링을 취소(clip)합니다.
        clip(texColor.a - _Cutoff); 

        return input.color;
      }
      ENDCG
    }
  }
}