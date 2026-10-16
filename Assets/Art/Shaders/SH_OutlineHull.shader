Shader "Game/OutlineHull"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth ("Outline Width (world units)", Range(0.005, 0.1)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "OutlineHull"
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Render back faces of a normal-expanded copy: only the rim
            // outside the real mesh survives the depth test.
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                half _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 expanded = i.positionOS.xyz + i.normalOS * _OutlineWidth;
                o.positionCS = TransformObjectToHClip(expanded);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
