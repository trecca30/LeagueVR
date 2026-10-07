Shader "LeagueVR/Rift Ground Fallback"
{
    Properties
    {
        _BaseMap("Existing Rift ground texture", 2D) = "white" {}
        _Tint("Tint", Color) = (0.75,0.8,0.72,1)
        _MetersPerTile("Metres per tile", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            float _MetersPerTile;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(i.normalOS); return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 weights=pow(abs(normalize(i.normal)),4); weights/=max(dot(weights,1),0.0001);
                float3 p=i.world/max(_MetersPerTile,.1);
                half3 col=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,frac(p.yz)).rgb*weights.x;
                col+=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,frac(p.xz)).rgb*weights.y;
                col+=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,frac(p.xy)).rgb*weights.z;
                return half4(col*_Tint.rgb,1);
            }
            ENDHLSL
        }
    }
}
