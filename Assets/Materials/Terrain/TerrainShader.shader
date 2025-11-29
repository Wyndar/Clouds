Shader "Custom/TerrainShaderURP"
{
    Properties
    {
        _Color("Color Tint", Color) = (1,1,1,1)
        _MainTex("Albedo", 2D) = "white" {}

        _NormalMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Float) = 1

        _Glossiness("Smoothness", Range(0,1)) = 0.5
        _Metallic("Metallic", Range(0,1)) = 0.0

        _MapScale("Triplanar Scale", Float) = 1

        _GridAlpha("Grid Alpha", Range(0,1)) = 1
        _GridCol("Grid Color", Color) = (1,1,1,1)
        _GridStep("Grid Step", Float) = 10
        _GridWidth("Grid Width", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            // -----------------------------------------------------
            // Uniforms
            // -----------------------------------------------------
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            float4 _Color;
            float _Glossiness;
            float _Metallic;
            float _MapScale;

            float _BumpScale;

            float4 _GridCol;
            float _GridStep;
            float _GridWidth;
            float _GridAlpha;

            // -----------------------------------------------------
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.worldNormal = normalize(TransformObjectToWorldNormal(IN.normalOS));
                OUT.positionCS = TransformWorldToHClip(OUT.worldPos);
                return OUT;
            }

            // -----------------------------------------------------
            float4 SampleTriplanarFloat4(float3 worldPos, float3 normal, float scale, Texture2D tex, SamplerState samp)
            {
                float3 n = normalize(abs(normal));
                n /= (n.x + n.y + n.z);

                float2 uvX = worldPos.yz * scale;
                float2 uvY = worldPos.zx * scale;
                float2 uvZ = worldPos.xy * scale;

                float4 x = tex.Sample(samp, uvX) * n.x;
                float4 y = tex.Sample(samp, uvY) * n.y;
                float4 z = tex.Sample(samp, uvZ) * n.z;

                return x + y + z;
            }

            float3 SampleTriplanarNormal(float3 worldPos, float3 normal, float scale)
            {
                float3 n = normalize(abs(normal));
                n /= (n.x + n.y + n.z);

                float2 uvX = worldPos.yz * scale;
                float2 uvY = worldPos.zx * scale;
                float2 uvZ = worldPos.xy * scale;

                float3 nx = UnpackNormal(_NormalMap.Sample(sampler_NormalMap, uvX)) * n.x;
                float3 ny = UnpackNormal(_NormalMap.Sample(sampler_NormalMap, uvY)) * n.y;
                float3 nz = UnpackNormal(_NormalMap.Sample(sampler_NormalMap, uvZ)) * n.z;

                return normalize((nx + ny + nz) * _BumpScale);
            }

            // -----------------------------------------------------
           half4 frag(Varyings IN) : SV_Target
            {
                float3 worldNormal = normalize(IN.worldNormal);
                float3 worldPos = IN.worldPos;

                // --- Triplanar Albedo ---
                float4 albedoTex = SampleTriplanarFloat4(worldPos, worldNormal, _MapScale, _MainTex, sampler_MainTex);
                albedoTex *= _Color;

                // --- Triplanar Normal ---
                float3 normalTS = SampleTriplanarNormal(worldPos, worldNormal, _MapScale);

                // --- Grid Overlay ---
                float2 pos = worldPos.xz / _GridStep;
                float2 f = abs(frac(pos) - 0.5);
                float2 df = fwidth(pos) * _GridWidth;
                float2 g = smoothstep(-df, df, f);
                float grid = 1.0 - saturate(g.x * g.y);

                float3 finalAlbedo = lerp(albedoTex.rgb, _GridCol.rgb, grid * _GridAlpha);


                // ---------------------------------------------------------
                //  SURFACE DATA (Material properties)
                // ---------------------------------------------------------
                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = finalAlbedo;
                surfaceData.metallic = _Metallic;
                surfaceData.specular = 0;
                surfaceData.smoothness = _Glossiness;
                surfaceData.normalTS = normalTS;
                surfaceData.emission = 0;
                surfaceData.occlusion = 1;


                // ---------------------------------------------------------
                //  INPUT DATA (Lighting + view)
                // ---------------------------------------------------------
                InputData inputData = (InputData)0;
                inputData.positionWS = worldPos;
                inputData.normalWS = normalize(worldNormal);
                inputData.viewDirectionWS = GetWorldSpaceViewDir(worldPos);

                // Shadows
                inputData.shadowCoord = TransformWorldToShadowCoord(worldPos);

                // Fog
                inputData.fogCoord = ComputeFogFactor(IN.positionCS.z);


                // ---------------------------------------------------------
                //  URP LIGHTING
                // ---------------------------------------------------------
                float3 lighting = UniversalFragmentPBR(inputData, surfaceData);

                lighting = MixFog(lighting, inputData.fogCoord);

                return float4(lighting, 1);
            }

            ENDHLSL
        }
    }
}
