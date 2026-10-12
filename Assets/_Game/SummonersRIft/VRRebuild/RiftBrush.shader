// Summoner's Rift brush: real blade geometry (crisp under MSAA, no alpha cards) in League's dark teal-to-yellow tufts.
// Vertex colour carries each blade's base tint and alpha its height fraction (0 at the root, 1 at the tip). Blades sway
// in the wind and bend away from whoever walks through (RiftBrushField sets _RiftBrushPush), and blades right in front
// of the eyes dissolve so the brush never blinds a VR player standing in it.
Shader "LeagueVR/Rift Brush"
{
    Properties
    {
        _TipColor ("Tip colour", Color) = (.5, .62, .24, 1)
        _Wind ("Wind sway (m)", Range(0, .3)) = .07
        _Light ("Sunlight amount", Range(0, 1)) = .45
    }

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TipColor;
                half _Wind, _Light;
            CBUFFER_END

            // Who walks through (xyz feet, w radius) and the distance haze, both set from scripts.
            float4 _RiftBrushPush;
            float4 _RiftHaze;
            half4 _RiftHazeColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                float h = input.color.a;
                float bend = h * h;
                float t = _Time.y;
                p.xz += float2(sin(t * 1.7 + p.x * .6 + p.z * .4), cos(t * 1.3 + p.x * .4 - p.z * .5)) * _Wind * bend;
                // Bend away from whoever walks through, and flatten a little.
                float2 away = p.xz - _RiftBrushPush.xz;
                float d = length(away);
                float push = _RiftBrushPush.w > 0 ? saturate(1 - d / _RiftBrushPush.w) : 0;
                if (push > 0 && abs(p.y - _RiftBrushPush.y) < 2.5)
                {
                    p.xz += away / max(d, 1e-3) * push * .45 * bend;
                    p.y -= push * .35 * bend;
                }
                output.positionWS = p;
                output.positionCS = TransformWorldToHClip(p);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 toEye = _WorldSpaceCameraPos - input.positionWS;
                float eye = length(toEye);
                // Dissolve blades within half a metre of the eyes (ordered dither, stable in stereo).
                if (eye < .55)
                {
                    float2 cell = floor(input.positionCS.xy);
                    float dither = frac(dot(cell, float2(.7548777, .5698403)) + .5);
                    clip(eye / .55 - dither);
                }
                float3 n = normalize(input.normalWS);
                n = front ? n : -n;
                half h = input.color.a;
                half3 albedo = lerp(input.color.rgb, _TipColor.rgb, h * h);
                Light sun = GetMainLight();
                half3 hue = sun.color / max(1e-3, max(sun.color.r, max(sun.color.g, sun.color.b)));
                half wrap = saturate(dot(n, sun.direction) * .5 + .5);
                // Light through the blades when looking toward the sun.
                half through = pow(saturate(dot(-normalize(toEye), sun.direction)), 4) * h * .2;
                half3 shade = lerp(half3(.5, .55, .5), hue * 1.0, wrap) + through * hue;
                // Roots sit in shade.
                half3 color = albedo * lerp(half3(1, 1, 1), shade, _Light) * lerp(.7, 1, h);
                float haze = saturate((eye - _RiftHaze.x) / max(1, _RiftHaze.y - _RiftHaze.x)) * _RiftHaze.z;
                return half4(lerp(color, _RiftHazeColor.rgb, haze), 1);
            }
            ENDHLSL
        }
    }
}
