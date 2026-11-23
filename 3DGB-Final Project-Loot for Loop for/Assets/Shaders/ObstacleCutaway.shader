Shader "LootForLoop/ObstacleCutaway" {
 Properties {
 [MainTexture] _BaseMap("Texture",2D)="white"{}
 [MainColor] _BaseColor("Color",Color)=(1,1,1,1)
 _Cull("Cull",Float)=2
 _Cutoff("Alpha cutoff",Range(0,1))=.5
 _Smoothness("Smoothness",Range(0,1))=0
 _Metallic("Metallic",Range(0,1))=0
 [HideInInspector] _CutawayMinY("Keep base below",Float)=-10000
 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 HLSLINCLUDE
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;float _Cull,_Cutoff,_Smoothness,_Metallic,_CutawayMinY;
 CBUFFER_END
 float4 _CutawayPlayer,_CutawayAim; // viewport xy, camera depth, enabled
 float4 _CutawaySettings; // player radius, aim radius, feather, aspect
 struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct Varyings {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;half3 normal:TEXCOORD1;float2 uv:TEXCOORD2;float4 screen:TEXCOORD3;UNITY_VERTEX_OUTPUT_STEREO};
 Varyings Vert(Attributes i){Varyings o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.screen=ComputeScreenPos(o.positionCS);o.normal=TransformObjectToWorldNormal(i.normalOS);o.uv=TRANSFORM_TEX(i.uv,_BaseMap);return o;}
 float Hole(float2 uv,float depth,float4 target,float radius){float2 delta=uv-target.xy;delta.x*=_CutawaySettings.w;float coverage=1-smoothstep(radius-_CutawaySettings.z,radius,length(delta));return coverage*target.w*step(depth+.15,target.z);}
 void Cut(Varyings i){if(i.world.y<=_CutawayMinY)return;float2 uv=i.screen.xy/i.screen.w;float depth=-TransformWorldToView(i.world).z;float coverage=max(Hole(uv,depth,_CutawayPlayer,_CutawaySettings.x),Hole(uv,depth,_CutawayAim,_CutawaySettings.y));float noise=frac(52.9829189*frac(dot(floor(i.positionCS.xy),float2(.06711056,.00583715))));clip(1-coverage-noise-.00001);}
 half4 Frag(Varyings i):SV_Target{Cut(i);half4 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv)*_BaseColor;
 #ifdef _ALPHATEST_ON
 clip(albedo.a-_Cutoff);
 #endif
 half3 normal=normalize(i.normal);Light light=GetMainLight(TransformWorldToShadowCoord(i.world));half3 illumination=SampleSH(normal)+light.color*saturate(dot(normal,light.direction))*light.shadowAttenuation*light.distanceAttenuation;half3 color=albedo.rgb*illumination;return half4(MixFog(color,ComputeFogFactor(TransformWorldToHClip(i.world).z)),1);}
 half4 Depth(Varyings i):SV_Target{Cut(i);return 0;}
 ENDHLSL
 Pass {
 Name "ForwardCutaway" Tags{"LightMode"="UniversalForwardOnly"} Cull[_Cull] ZWrite On
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
 #pragma multi_compile_fog
 #pragma shader_feature_local_fragment _ALPHATEST_ON
 ENDHLSL
 }
 Pass {
 Name "DepthOnly" Tags{"LightMode"="DepthOnly"} Cull[_Cull] ZWrite On ColorMask R
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Depth
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 }
}
