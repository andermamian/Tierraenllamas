// TIERRA EN LLAMAS - Shader de Vegetación Selvática
// Shader URP personalizado para la selva del Putumayo
// Incluye: movimiento por viento, subsurface scattering, variación de color,
// interacción con lluvia (gotas, brillo húmedo)

Shader "TierraEnLlamas/SelvaFoliage"
{
    Properties
    {
        _MainTex ("Textura Principal", 2D) = "white" {}
        _NormalMap ("Mapa Normal", 2D) = "bump" {}
        _Color ("Color Base", Color) = (0.2, 0.5, 0.1, 1)
        _ColorVariation ("Variación de Color", Range(0, 0.3)) = 0.1
        
        [Header(Subsurface Scattering)]
        _SubsurfaceColor ("Color Subsuperficial", Color) = (0.4, 0.8, 0.2, 1)
        _SubsurfaceIntensity ("Intensidad SSS", Range(0, 2)) = 0.5
        _Translucency ("Translucidez", Range(0, 1)) = 0.3
        
        [Header(Viento)]
        _WindStrength ("Fuerza del Viento", Range(0, 2)) = 0.5
        _WindSpeed ("Velocidad del Viento", Range(0, 5)) = 1.5
        _WindDirection ("Dirección del Viento", Vector) = (1, 0, 0.5, 0)
        _TrunkStiffness ("Rigidez del Tronco", Range(0, 1)) = 0.8
        
        [Header(Lluvia)]
        _Wetness ("Humedad", Range(0, 1)) = 0
        _RainDroplets ("Gotas de Lluvia", 2D) = "black" {}
        _DropletSpeed ("Velocidad Gotas", Range(0, 5)) = 2
        _WetSmoothness ("Suavidad Húmeda", Range(0, 1)) = 0.8
        
        [Header(Rendering)]
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _OcclusionMap ("Mapa de Oclusión", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "TransparentCutout" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
        }
        
        LOD 200
        Cull Off // Doble cara para hojas
        
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float4 color : COLOR; // Vertex color para pintar peso del viento
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 bitangentWS : TEXCOORD4;
                float fogFactor : TEXCOORD5;
                float4 color : COLOR;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);
            TEXTURE2D(_RainDroplets);
            SAMPLER(sampler_RainDroplets);
            TEXTURE2D(_OcclusionMap);
            SAMPLER(sampler_OcclusionMap);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float _ColorVariation;
                float4 _SubsurfaceColor;
                float _SubsurfaceIntensity;
                float _Translucency;
                float _WindStrength;
                float _WindSpeed;
                float4 _WindDirection;
                float _TrunkStiffness;
                float _Wetness;
                float _DropletSpeed;
                float _WetSmoothness;
                float _Cutoff;
            CBUFFER_END
            
            // Variables globales del WeatherSystem
            float4 _GlobalWindDirection;
            float _GlobalWindStrength;
            float _GlobalWetness;
            
            // Función de ruido simple para viento
            float SimpleNoise(float3 pos)
            {
                return frac(sin(dot(pos, float3(12.9898, 78.233, 45.5432))) * 43758.5453);
            }
            
            // Desplazamiento por viento
            float3 WindDisplacement(float3 positionWS, float windWeight)
            {
                float time = _Time.y * _WindSpeed;
                float3 windDir = normalize(_WindDirection.xyz + _GlobalWindDirection.xyz);
                float strength = (_WindStrength + _GlobalWindStrength) * windWeight;
                
                // Movimiento principal
                float sway = sin(time + positionWS.x * 0.5 + positionWS.z * 0.3) * strength;
                
                // Micro-movimiento (hojas individuales)
                float micro = sin(time * 3.7 + positionWS.y * 2.1) * strength * 0.3;
                
                // Ráfagas ocasionales
                float gust = pow(max(0, sin(time * 0.2)), 4) * strength * 2;
                
                return windDir * (sway + micro + gust);
            }
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Peso del viento desde vertex color (R = peso)
                float windWeight = input.color.r * (1.0 - _TrunkStiffness);
                
                // Aplicar desplazamiento por viento
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 windOffset = WindDisplacement(posWS, windWeight);
                input.positionOS.xyz += TransformWorldToObjectDir(windOffset);
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                output.bitangentWS = cross(output.normalWS, output.tangentWS) * input.tangentOS.w;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                output.color = input.color;
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Textura base con variación de color
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // Alpha test
                clip(texColor.a - _Cutoff);
                
                // Variación de color por posición (diversidad natural)
                float colorNoise = SimpleNoise(input.positionWS * 0.5);
                half3 baseColor = texColor.rgb * _Color.rgb;
                baseColor = lerp(baseColor, baseColor * (1 + _ColorVariation), colorNoise);
                
                // Normal mapping
                half3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv));
                float3x3 TBN = float3x3(input.tangentWS, input.bitangentWS, input.normalWS);
                half3 normalWS = normalize(mul(normalTS, TBN));
                
                // Iluminación principal
                Light mainLight = GetMainLight();
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = baseColor * mainLight.color * NdotL;
                
                // Subsurface Scattering (luz a través de hojas)
                half3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                half VdotL = saturate(dot(viewDir, -mainLight.direction));
                half3 subsurface = _SubsurfaceColor.rgb * pow(VdotL, 3) * _SubsurfaceIntensity;
                subsurface *= _Translucency * texColor.a;
                
                // Efecto de humedad/lluvia
                float wetness = max(_Wetness, _GlobalWetness);
                half3 wetColor = baseColor * 0.7; // Más oscuro cuando está mojado
                baseColor = lerp(baseColor, wetColor, wetness);
                
                // Gotas de lluvia animadas
                if (wetness > 0.1)
                {
                    float2 dropUV = input.uv * 4 + float2(0, _Time.y * _DropletSpeed);
                    half droplets = SAMPLE_TEXTURE2D(_RainDroplets, sampler_RainDroplets, dropUV).r;
                    baseColor += droplets * wetness * 0.2;
                }
                
                // Oclusión ambiental
                half ao = SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).r;
                
                // Color final
                half3 finalColor = (diffuse + subsurface) * ao;
                finalColor += baseColor * 0.1; // Ambiente mínimo
                
                // Niebla
                finalColor = MixFog(finalColor, input.fogFactor);
                
                return half4(finalColor, texColor.a);
            }
            ENDHLSL
        }
        
        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float _Cutoff;
            
            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            
            half4 ShadowFrag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                clip(tex.a - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Lit"
}
