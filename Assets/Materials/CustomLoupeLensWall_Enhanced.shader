Shader "Custom/LoupeLensWall_Enhanced"
{
    Properties
    {
        _MainTex ("RT", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Radius ("Radius", Range(0,1)) = 0.9
        _Edge ("Edge Softness", Range(0,0.5)) = 0.05
        _AngleCorrection ("Angle Correction (rad)", Float) = 0

        _Distortion ("Radial Distortion", Range(0,1)) = 0.2
        _Chromatic ("Chromatic Aberration", Range(0,0.05)) = 0.01
        _Vignette ("Vignette Strength", Range(0,1)) = 0.2
        _RimColor ("Lens Rim Color", Color) = (1,1,1,0.3)
        _RimWidth ("Lens Rim Width", Range(0,0.2)) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="Geometry+10" "RenderType"="Opaque" }
        ZWrite On
        ZTest LEqual
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _Color;
            float _Radius;
            float _Edge;
            float _AngleCorrection;

            float _Distortion;
            float _Chromatic;
            float _Vignette;
            float4 _RimColor;
            float _RimWidth;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // UV centrées (-1..1)
                float2 uvCentered = i.uv * 2.0 - 1.0;
                float dist = length(uvCentered);

                // Masque circulaire
                if (dist > _Radius + _RimWidth) discard;

                // Correction de rotation
                float c = cos(-_AngleCorrection);
                float s = sin(-_AngleCorrection);
                float2 uvRot;
                uvRot.x = uvCentered.x * c - uvCentered.y * s;
                uvRot.y = uvCentered.x * s + uvCentered.y * c;

                float r = length(uvRot);

                // Distorsion radiale
                float factor = 1.0 + _Distortion * r * r;
                uvRot *= factor;

                // Revenir en UV [0..1]
                float2 uvFinal = uvRot * 0.5 + 0.5;

                // Aberration chromatique
                float2 offset = uvRot * _Chromatic * r;
                float rCol = tex2D(_MainTex, uvFinal + offset).r;
                float gCol = tex2D(_MainTex, uvFinal).g;
                float bCol = tex2D(_MainTex, uvFinal - offset).b;
                fixed4 col = fixed4(rCol, gCol, bCol, 1.0);

                // Teinte
                col *= _Color;

                // Vignetage (assombrir vers les bords)
                float vignette = smoothstep(_Radius, _Radius * 0.7, r);
                col.rgb *= (1.0 - _Vignette * vignette);

                // Bord de lentille (anneau semi-transparent)
                if (dist > _Radius && dist <= _Radius + _RimWidth)
                {
                    float t = smoothstep(_Radius + _RimWidth, _Radius, dist);
                    col = lerp(_RimColor, col, t);
                }

                return col;
            }
            ENDCG
        }
    }
}
