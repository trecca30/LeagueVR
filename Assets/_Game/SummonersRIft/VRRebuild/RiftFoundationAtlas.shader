Shader "LeagueVR/Rift Foundation Atlas" {
 Properties { _BaseMap("Original Rift terrain atlas",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) }
 SubShader {Tags{"RenderType"="Opaque" "RenderPipeline"="UniversalPipeline"} Cull Off ZWrite On
 Pass {Tags{"LightMode"="SRPDefaultUnlit"}
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 float4 _RiftHaze;half4 _RiftHazeColor;
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;
 CBUFFER_END
 struct Attributes{float4 positionOS:POSITION;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct Varyings{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
 Varyings Vert(Attributes i){Varyings o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
 half4 Frag(Varyings i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);float2 rawGrid=(i.world.xz-float2(-71.65,-59.50))/27.46732;float2 grid=clamp(rawGrid,.00001,4.99999);float2 cell=floor(grid);float slice=cell.y*5+4-cell.x;float2 uv=1-frac(grid);float2 tile=float2(fmod(slice,5),4-floor(slice/5));if(any(rawGrid<0)||any(rawGrid>=5)){tile=float2(4,4);uv=float2(.1,.35)+frac(i.world.xz*.125)*float2(.23,.2);}float2 atlas=(tile*520+4+uv*512)/2600;half3 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,atlas).rgb*_BaseColor.rgb;float haze=saturate((distance(i.world,_WorldSpaceCameraPos)-_RiftHaze.x)/max(1,_RiftHaze.y-_RiftHaze.x))*_RiftHaze.z;return half4(lerp(color,_RiftHazeColor.rgb,haze),1);}
 ENDHLSL
 } } }


