// Darkens everything behind its front faces (multiply). Used as a box over locked rooms.
Shader "Library/Room Darkness"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.2, 0.2, 0.3, 1)
        _Strength ("Strength", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Darken"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                return half4(lerp(half3(1, 1, 1), _Tint.rgb, _Strength), 1);
            }
            ENDHLSL
        }
    }
}
