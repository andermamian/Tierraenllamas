// TIERRA EN LLAMAS - Shader de Agua Fluvial
// Shader URP para ríos amazónicos con:
// - Reflejos y refracciones
// - Corriente con dirección
// - Espuma en orillas
// - Color turbio tropical
// - Interacción con lluvia

Shader "TierraEnLlamas/RiverWater"
{
    Properties
    {
        [Header(Color del Agua)]
        _ShallowColor ("Color Superficial", Color) = (0.2, 0.4, 0.15, 0.8)
        _DeepColor ("Color Profundo", Color) = (0.05, 0.15, 0.05, 0.95)
        _DepthFade ("Profundidad de Fade", Range(0.1, 10)) = 3
        _Turbidity ("Turbidez", Range(0, 1)) = 0.6
        
        [Header(Oleaje)]
        _WaveNormal1 ("Normal de Olas 1", 2D) = "bump" {}
        _WaveNormal2 ("Normal de Olas 2", 2D) = "bump" {}
        _WaveSpeed1 ("Velocidad Ola 1", Vector) = (0.05, 0.03, 0, 0)
        _WaveSpeed2 ("Velocidad Ola 2", Vector) = (-0.03, 0.05, 0, 0)
        _WaveScale ("Escala de Olas", Range(0.1, 5)) = 1
        _WaveStrength ("Fuerza de Olas", Range(0, 1)) = 0.3
        
        [Header(Corriente)]
        _FlowDirection ("Dirección de Corriente", Vector) = (1, 0, 0, 0)
        _FlowSpeed ("Velocidad de Corriente", Range(0, 3)) = 0.5
        _FlowDistortion ("Distorsión", Range(0, 0.5)) = 0.1
        
        [Header(Reflejos)]
        _ReflectionStrength ("Fuerza de Reflejo", Range(0, 1)) = 0.5
        _FresnelPower ("Potencia Fresnel", Range(1, 10)) = 4
        _Smoothness ("Suavidad", Range(0, 1)) = 0.9
        
        [Header(Espuma)]
        _FoamTex ("Textura de Espuma", 2D) = "white" {}
        _FoamColor ("Color de Espuma", Color) = (0.9, 0.9, 0.8, 1)
        _FoamThreshold ("Umbral de Espuma", Range(0, 2)) = 0.5
        _FoamWidth ("Ancho de Espuma", Range(0, 3)) = 1
        
        [Header(Lluvia)]
        _RainIntensity ("Intensidad de Lluvia", Range(0, 1)) = 0
        _RipplesTex ("Textura de Ondas", 2D) = "black" {}
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }
        
        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };
            
            TEXTURE2D(_WaveNormal1);    SAMPLER(sampler_WaveNormal1);
            TEXTURE2D(_WaveNormal2);    SAMPLER(sampler_WaveNormal2);
            TEXTURE2D(_FoamTex);        SAMPLER(sampler_FoamTex);
            TEXTURE2D(_RipplesTex);     SAMPLER(sampler_RipplesTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float _DepthFade;
                float _Turbidity;
                float4 _WaveSpeed1;
                float4 _WaveSpeed2;
                float _WaveScale;
                float _WaveStrength;
                float4 _FlowDirection;
                float _FlowSpeed;
                float _FlowDistortion;
                float _ReflectionStrength;
                float _FresnelPower;
                float _Smoothness;
                float4 _FoamColor;
                float _FoamThreshold;
                float _FoamWidth;
                float _RainIntensity;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // Desplazamiento vertical por oleaje
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                float wave = sin(_Time.y * 0.5 + worldPos.x * 0.3 + worldPos.z * 0.2) * 0.1;
                wave += sin(_Time.y * 0.8 + worldPos.x * 0.5) * 0.05;
                input.positionOS.y += wave * _WaveStrength;
                
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // UV con corriente
                float2 flowUV = input.uv + _FlowDirection.xz * _Time.y * _FlowSpeed;
                
                // Normales de olas (dos capas animadas)
                float2 uv1 = flowUV * _WaveScale + _Time.y * _WaveSpeed1.xy;
                float2 uv2 = flowUV * _WaveScale * 0.7 + _Time.y * _WaveSpeed2.xy;
                
                half3 normal1 = UnpackNormal(SAMPLE_TEXTURE2D(_WaveNormal1, sampler_WaveNormal1, uv1));
                half3 normal2 = UnpackNormal(SAMPLE_TEXTURE2D(_WaveNormal2, sampler_WaveNormal2, uv2));
                half3 waterNormal = normalize(half3(
                    (normal1.xy + normal2.xy) * _WaveStrength,
                    1
                ));
                
                // Profundidad del agua
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterDepth = sceneDepth - input.screenPos.w;
                float depthFade = saturate(waterDepth / _DepthFade);
                
                // Color del agua basado en profundidad
                half4 waterColor = lerp(_ShallowColor, _DeepColor, depthFade);
                
                // Turbidez (agua amazónica marrón-verdosa)
                waterColor.rgb = lerp(waterColor.rgb, waterColor.rgb * 0.6, _Turbidity * depthFade);
                
                // Fresnel para reflejos
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float fresnel = pow(1.0 - saturate(dot(viewDir, waterNormal)), _FresnelPower);
                
                // Reflejos del cielo/entorno
                half3 reflectionDir = reflect(-viewDir, waterNormal);
                half3 skyColor = half3(0.3, 0.4, 0.5); // Simplificado
                half3 reflection = skyColor * fresnel * _ReflectionStrength;
                
                // Iluminación
                Light mainLight = GetMainLight();
                half3 specular = pow(saturate(dot(reflect(-mainLight.direction, waterNormal), viewDir)), 64) * mainLight.color;
                
                // Espuma en orillas
                half foam = 0;
                if (waterDepth < _FoamWidth)
                {
                    float foamFade = 1.0 - (waterDepth / _FoamWidth);
                    float2 foamUV = flowUV * 3 + _Time.y * 0.1;
                    foam = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, foamUV).r;
                    foam *= foamFade;
                    foam = step(_FoamThreshold, foam) * foamFade;
                }
                
                // Ondas de lluvia
                half3 rainRipples = half3(0, 0, 0);
                if (_RainIntensity > 0.01)
                {
                    float2 rippleUV = input.uv * 8 + _Time.y * 0.5;
                    half ripple = SAMPLE_TEXTURE2D(_RipplesTex, sampler_RipplesTex, rippleUV).r;
                    rainRipples = ripple * _RainIntensity * 0.3;
                }
                
                // Composición final
                half3 finalColor = waterColor.rgb;
                finalColor += reflection;
                finalColor += specular * 0.5;
                finalColor += rainRipples;
                finalColor = lerp(finalColor, _FoamColor.rgb, foam);
                
                // Niebla
                finalColor = MixFog(finalColor, input.fogFactor);
                
                // Alpha basado en profundidad
                float alpha = lerp(waterColor.a * 0.6, waterColor.a, depthFade);
                alpha = max(alpha, foam * 0.9);
                
                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
    
    FallBack "Universal Render Pipeline/Lit"
}
