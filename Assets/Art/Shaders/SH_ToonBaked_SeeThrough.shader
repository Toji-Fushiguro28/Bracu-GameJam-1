// Walls: same look as SG_ToonBaked (baked light -> luminance -> posterize -> tint lerp x base + emission + rim),
// plus a dithered circular see-through hole around the player when a wall is between camera and player.
// The hole is driven by globals set by SeeThroughCutout.cs on the camera.
Shader "Library/ToonBaked SeeThrough"
{
    Properties
    {
        _Base_Map ("Base Map", 2D) = "white" {}
        _Emission_Map ("Emission Map", 2D) = "black" {}
        [HDR] _Emission_Color ("Emission Color", Color) = (1, 1, 1, 1)
        _Shadow_Tint ("Shadow Tint", Color) = (0.5, 0.45, 0.33, 1)
        _Light_Tint ("Light Tint", Color) = (1, 0.93, 0.8, 1)
        _Steps ("Steps", Range(1, 64)) = 6
        _Light_Boost ("Light Boost", Float) = 1
        _Rim_Color ("Rim Color", Color) = (0, 0, 0, 1)
        _Rim_Power ("Rim Power", Range(0.5, 8)) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_Base_Map);     SAMPLER(sampler_Base_Map);
        TEXTURE2D(_Emission_Map); SAMPLER(sampler_Emission_Map);

        CBUFFER_START(UnityPerMaterial)
            float4 _Base_Map_ST;
            float4 _Emission_Map_ST;
            half4 _Emission_Color;
            half4 _Shadow_Tint;
            half4 _Light_Tint;
            half _Steps;
            half _Light_Boost;
            half4 _Rim_Color;
            half _Rim_Power;
        CBUFFER_END

        // Set globally by SeeThroughCutout.cs
        float4 _SeeThrough_Center;   // xy = screen uv of the player
        float _SeeThrough_Radius;    // fraction of screen height
        float _SeeThrough_Strength;  // 0..1 fade
        float _SeeThrough_Depth;     // player's view depth
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float  fog        : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.lightmapUV = v.lightmapUV * unity_LightmapST.xy + unity_LightmapST.zw;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // 4x4 Bayer matrix for a soft, opaque-friendly fade
            float Bayer4(float2 p)
            {
                uint2 i = uint2(p) % 4;
                const float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
                return (m[i.y * 4 + i.x] + 0.5) / 16.0;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // See-through hole: only for wall pixels between the camera and the player
                if (_SeeThrough_Strength > 0.001)
                {
                    float viewDepth = -TransformWorldToView(i.positionWS).z;
                    if (viewDepth < _SeeThrough_Depth - 0.25)
                    {
                        float2 screenUV = i.positionCS.xy / _ScaledScreenParams.xy;
                        float2 d = screenUV - _SeeThrough_Center.xy;
                        d.x *= _ScaledScreenParams.x / _ScaledScreenParams.y;
                        float r = _SeeThrough_Radius * _SeeThrough_Strength;
                        // 1 inside the hole, fading to 0 at the rim
                        float hole = 1.0 - smoothstep(r * 0.55, r, length(d));
                        clip(Bayer4(i.positionCS.xy) - hole);
                    }
                }

                float3 normalWS = normalize(i.normalWS);
                #if defined(LIGHTMAP_ON)
                    half3 gi = SampleLightmap(i.lightmapUV, half3(normalWS));
                #else
                    half3 gi = SampleSH(normalWS);
                #endif
                half lum = saturate(dot(gi * _Light_Boost, half3(0.2126, 0.7152, 0.0722)));
                half stepped = floor(lum * _Steps) / max(_Steps, 1.0);
                half3 tint = lerp(_Shadow_Tint.rgb, _Light_Tint.rgb, stepped);

                half3 baseCol = SAMPLE_TEXTURE2D(_Base_Map, sampler_Base_Map, TRANSFORM_TEX(i.uv, _Base_Map)).rgb;
                half3 emission = SAMPLE_TEXTURE2D(_Emission_Map, sampler_Emission_Map, TRANSFORM_TEX(i.uv, _Emission_Map)).rgb * _Emission_Color.rgb;
                float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(dot(normalWS, viewDir)), _Rim_Power);

                half3 col = tint * baseCol + emission + rim * _Rim_Color.rgb;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Lets the lightmapper read albedo/emission so baking matches the original material
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vertMeta
            #pragma fragment fragMeta
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            struct MetaAttributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            struct MetaVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            MetaVaryings vertMeta(MetaAttributes v)
            {
                MetaVaryings o;
                o.positionCS = UnityMetaVertexPosition(v.positionOS.xyz, v.uv1, v.uv2);
                o.uv = v.uv0;
                return o;
            }

            half4 fragMeta(MetaVaryings i) : SV_Target
            {
                MetaInput m = (MetaInput)0;
                m.Albedo = SAMPLE_TEXTURE2D(_Base_Map, sampler_Base_Map, TRANSFORM_TEX(i.uv, _Base_Map)).rgb;
                m.Emission = SAMPLE_TEXTURE2D(_Emission_Map, sampler_Emission_Map, TRANSFORM_TEX(i.uv, _Emission_Map)).rgb * _Emission_Color.rgb;
                return UnityMetaFragment(m);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
