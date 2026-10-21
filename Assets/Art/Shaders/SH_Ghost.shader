Shader "Game/Ghost"
{
    Properties
    {
        _Color ("Body (rgb) and opacity (a)", Color) = (0.62, 0.82, 1, 0.22)
        _RimColor ("Edge glow", Color) = (0.85, 0.96, 1, 0.75)
        _RimPower ("Edge sharpness", Range(0.5, 8)) = 2.2
        _Glow ("Glow strength", Range(1, 3)) = 1.6
        _Alpha ("Fade (set by script)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        // Depth first, so only the nearest surface of the body shows (no tangle of inner faces).
        Pass
        {
            Name "GhostDepth"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        // A pale see-through body with a brighter rim: a soft glow, no lighting.
        Pass
        {
            Name "Ghost"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _Glow;
                half _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half rim = pow(1 - saturate(dot(normalize(i.normalWS), normalize(i.viewWS))), _RimPower);
                half3 col = (_Color.rgb + _RimColor.rgb * rim) * _Glow;
                half a = saturate(_Color.a + _RimColor.a * rim) * _Alpha;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
