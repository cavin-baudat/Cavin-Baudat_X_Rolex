Shader "Hidden/Magnifier/ScreenZoomURP"
{
    Properties
    {
        _Opacity ("Opacity", Range(0,1)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
        _Center ("Center (UV)", Vector) = (0.5,0.5,0,0)
        _Radius ("Radius (UV)", Range(0,1)) = 0.12
        _Feather ("Feather (UV)", Range(0,0.5)) = 0.08
        _Magnification ("Magnification", Range(1,8)) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        ZWrite Off ZTest Always Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "ScreenZoom"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile __ MAG_USE_MAIN_TEX
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Blit source (URP)
            #if defined(MAG_USE_MAIN_TEX)
                TEXTURE2D(_MainTex);
                SAMPLER(sampler_MainTex);
            #else
                TEXTURE2D_X(_BlitTexture);
                SAMPLER(sampler_BlitTexture);
            #endif

            float _Opacity;
            float4 _Tint;
            float2 _Center;
            float _Radius;
            float _Feather;
            float _Magnification;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings Vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 SampleSource(float2 uv)
            {
                #if defined(MAG_USE_MAIN_TEX)
                    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                #else
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
                #endif
            }

            half4 Frag (Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // Cercle visuel indépendant du ratio
                float2 aspect = float2(_ScreenParams.x, _ScreenParams.y);
                float minDim = min(aspect.x, aspect.y);
                float2 scale = aspect / minDim;

                float2 uvToCenter = (uv - _Center) * scale;
                float dist = length(uvToCenter);

                float r = _Radius;
                float f = max(1e-4, _Feather);
                float mask = saturate(1.0 - smoothstep(r - f, r, dist));

                float m = max(1.0, _Magnification);
                float2 zoomedUV = _Center + (uv - _Center) / m;

                half4 srcCol = SampleSource(uv);
                half4 zoomCol = SampleSource(zoomedUV) * _Tint;

                float a = mask * _Opacity;
                half4 outCol = lerp(srcCol, zoomCol, a);
                outCol.a = 1;
                return outCol;
            }
            ENDHLSL
        }
    }
    FallBack Off
}