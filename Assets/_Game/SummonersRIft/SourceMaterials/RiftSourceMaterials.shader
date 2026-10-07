Shader "LeagueVR/Rift Source Materials"
{
    Properties
    {
        _SourceTextures("Original map textures", 2DArray)=""{}
        _VertexMaterials("Original material and UV lookup",2D)="white"{}
        _LookupWidth("Lookup width",Float)=256
        _LookupHeight("Lookup height",Float)=1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Cull Off ZWrite On
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_ARRAY(_SourceTextures); SAMPLER(sampler_SourceTextures);
            TEXTURE2D(_VertexMaterials);
            CBUFFER_START(UnityPerMaterial)
            float _LookupWidth; float _LookupHeight;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; uint vertexID:SV_VertexID; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; nointerpolation float2 material:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                int2 cell=int2(v.vertexID%(uint)_LookupWidth,v.vertexID/(uint)_LookupWidth);
                float4 assignment=LOAD_TEXTURE2D(_VertexMaterials,cell);
                o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
                o.uv=assignment.xy; o.material=assignment.zw; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 color=SAMPLE_TEXTURE2D_ARRAY(_SourceTextures,sampler_SourceTextures,i.uv,i.material.x);
                if(i.material.y>.5) clip(color.a-.35);
                return half4(color.rgb,1);
            }
            ENDHLSL
        }
    }
}
