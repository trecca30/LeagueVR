// Summoner's Rift's painted textures, lit for VR. League paints its map for a camera high above, unlit; walked through
// in VR it reads flat, and cliff faces show the paint stretched down them. This keeps the painting in charge and adds:
// - soft, banded sunlight (a stylized cel look that never fights the painted light),
// - relief from the painting itself (its brightness as a bump map, so stones and roots catch the light),
// - steep faces cleaned up: the stretched streaks are softened and broken up with a little rock grain,
// - a sky-coloured rim on silhouettes and a distance haze (RiftAtmosphere).
// Double-sided like the export (its triangles are mirrored), with optional alpha clipping for foliage.
Shader "LeagueVR/Rift Stylized"
{
    Properties
    {
        _BaseMap ("Painted texture", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Light ("Sunlight amount", Range(0, 1)) = .35
        _Bands ("Light bands", Range(1, 6)) = 3
        _Relief ("Relief from the painting", Range(0, 4)) = 1.4
        _Steep ("Cliff stretch cleanup", Range(0, 1)) = .65
        _Grain ("Cliff rock grain", Range(0, 1)) = .35
        _Rim ("Sky rim light", Range(0, 1)) = .18
        _Cutoff ("Alpha cutoff", Range(0, 1)) = .5
        [Toggle(_ALPHATEST_ON)] _AlphaClip ("Alpha clip", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap);
    SAMPLER(sampler_BaseMap);

    // Distance haze from RiftAtmosphere: start, end (metres), strength.
    float4 _RiftHaze;
    half4 _RiftHazeColor;

    half3 RiftHaze(half3 color, float3 positionWS)
    {
        float amount = saturate((distance(positionWS, _WorldSpaceCameraPos) - _RiftHaze.x) / max(1, _RiftHaze.y - _RiftHaze.x)) * _RiftHaze.z;
        return lerp(color, _RiftHazeColor.rgb, amount);
    }

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseMap_TexelSize;
        half4 _BaseColor;
        half _Light, _Bands, _Relief, _Steep, _Grain, _Rim, _Cutoff;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half Luma(half3 c) { return dot(c, half3(.299, .587, .114)); }

            float Hash(float3 p)
            {
                p = frac(p * .3183099 + .1);
                p *= 17;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Smooth 3D value noise for the cliff grain.
            float Noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                #if _ALPHATEST_ON
                    clip(albedo.a - _Cutoff);
                #endif
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 n = normalize(input.normalWS);
                // The export's faces are mirrored and drawn from both sides: light the side we are looking at.
                n = dot(n, viewDir) < 0 ? -n : n;

                // Steep faces: League's top-down painting smears down cliffs. Soften the smear (a blurrier mip) and
                // break it up with rock grain in world space.
                float steep = saturate(1 - abs(n.y) * 1.5) * _Steep;
                if (steep > .001)
                {
                    half3 soft = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, input.uv, 1.5).rgb * _BaseColor.rgb;
                    float grain = Noise(input.positionWS * 1.7) * .6 + Noise(input.positionWS * 5.3) * .4;
                    soft *= lerp(1, .85 + grain * .3, _Grain);
                    albedo.rgb = lerp(albedo.rgb, soft, steep);
                }

                // Relief: the painting's brightness slope as a bump, on a tangent frame from screen derivatives.
                float2 texel = _BaseMap_TexelSize.xy * 1.5;
                half hx = Luma(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + float2(texel.x, 0)).rgb) - Luma(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv - float2(texel.x, 0)).rgb);
                half hy = Luma(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv + float2(0, texel.y)).rgb) - Luma(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv - float2(0, texel.y)).rgb);
                float3 dp1 = ddx(input.positionWS), dp2 = ddy(input.positionWS);
                float2 duv1 = ddx(input.uv), duv2 = ddy(input.uv);
                float3 dp2perp = cross(dp2, n), dp1perp = cross(n, dp1);
                float3 t = dp2perp * duv1.x + dp1perp * duv2.x;
                float3 b = dp2perp * duv1.y + dp1perp * duv2.y;
                float scale = rsqrt(max(1e-12, max(dot(t, t), dot(b, b))));
                float3 bumped = normalize(n - (t * scale * hx + b * scale * hy) * _Relief);

                // Banded sunlight: a soft cel ramp, mixed in lightly so the painted shading still leads. Only the sun's
                // hue is used, so a dim scene light never greys the painting out.
                Light sun = GetMainLight();
                half3 hue = sun.color / max(1e-3, max(sun.color.r, max(sun.color.g, sun.color.b)));
                float wrap = saturate(dot(bumped, sun.direction) * .5 + .5);
                float ramp = wrap * _Bands;
                float banded = (floor(ramp) + smoothstep(.35, .65, frac(ramp))) / _Bands;
                half3 shade = lerp(half3(.8, .84, .93), hue * 1.14, banded);
                half3 color = albedo.rgb * lerp(half3(1, 1, 1), shade, _Light);
                // A touch more colour, as League's art reads in its own lighting.
                color = lerp(Luma(color).xxx, color, 1.08);

                // Sky rim on silhouettes.
                half rim = pow(1 - saturate(dot(n, viewDir)), 3) * _Rim;
                color += rim * half3(.55, .7, .85);
                // Haze by true distance per pixel (League's floor tiles are tens of metres across; anything per
                // vertex would haze the ground at your feet).
                return half4(RiftHaze(color, input.positionWS), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                #if _ALPHATEST_ON
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half Frag(Varyings input) : SV_Target
            {
                #if _ALPHATEST_ON
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _Cutoff);
                #endif
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
