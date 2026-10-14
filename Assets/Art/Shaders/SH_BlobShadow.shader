Shader "Game/BlobShadow"
{
    Properties
    {
        _ShadowColor ("Shadow Color", Color) = (0.35, 0.24, 0.42, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.6
        _Hardness ("Hardness", Range(0.5, 6)) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "BlobShadow"
            Tags { "LightMode"="UniversalForward" }
            // Multiply blend: darkens whatever is underneath (keeps floor hue), order independent.
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShadowColor;
                half _Opacity;
                half _Hardness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv * 2.0 - 1.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half d = saturate(1.0 - dot(i.uv, i.uv));          // radial falloff, no texture needed
                half a = pow(d, _Hardness) * _Opacity;
                return half4(lerp(half3(1, 1, 1), _ShadowColor.rgb, a), 1);
            }
            ENDHLSL
        }
    }
}
