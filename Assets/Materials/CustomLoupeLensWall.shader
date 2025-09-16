Shader "Custom/LoupeLensWall"
{
    Properties
    {
        _MainTex ("RT", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Radius ("Radius", Range(0,1)) = 0.9
        _Edge ("Edge Softness", Range(0,0.5)) = 0.0
        _AngleCorrection ("Angle Correction (rad)", Float) = 0
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

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // UV centrees (-1..1)
                float2 uvCentered = i.uv * 2.0 - 1.0;

                // Rotation inverse pour compenser le roll dans l'espace vue
                float c = cos(-_AngleCorrection);
                float s = sin(-_AngleCorrection);
                float2 uvRot;
                uvRot.x = uvCentered.x * c - uvCentered.y * s;
                uvRot.y = uvCentered.x * s + uvCentered.y * c;

                // Masque circulaire
                float dist = length(uvRot);
                if (dist > _Radius) discard;

                float2 uvFinal = uvRot * 0.5 + 0.5;
                fixed4 col = tex2D(_MainTex, uvFinal) * _Color;
                return col;
            }
            ENDCG
        }
    }
}
