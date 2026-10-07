Shader "LeagueVR/Rift Recovered Terrain"
{
    Properties { _Tiles("Rift terrain tiles",2DArray)=""{} _MapOrigin("Map origin",Vector)=(3,0,3,0) _MapScale("Map scale",Float)=6 }
    SubShader
    {
        Tags {"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"}
        Cull Off ZWrite On
        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D_ARRAY(_Tiles);SAMPLER(sampler_Tiles);
            CBUFFER_START(UnityPerMaterial)
            float4 _MapOrigin;float _MapScale;
            CBUFFER_END
            struct Attributes{float4 positionOS:POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            Varyings Vert(Attributes i){Varyings o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 local=(i.world.xz-_MapOrigin.xz)/_MapScale;
                float2 grid=clamp((local-float2(-12.441667,-10.416912))/4.577887,0.00001,4.99999);
                float2 cell=floor(grid);float slice=cell.y*5+4-cell.x;
                float2 uv=1-frac(grid);
                return half4(SAMPLE_TEXTURE2D_ARRAY(_Tiles,sampler_Tiles,uv,slice).rgb,1);
            }
            ENDHLSL
        }
    }
}
