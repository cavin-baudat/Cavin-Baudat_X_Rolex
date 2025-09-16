Shader "Magnifier/OverlayCircular"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Opacity ("Opacity", Range(0,1)) = 1
        _Center ("Center", Vector) = (0.5, 0.5, 0, 0)
        _Radius ("Radius", Range(0,1)) = 0.5
        _Feather ("Feather", Range(0,0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST;
            float4 _Tint; float _Opacity; float2 _Center; float _Radius; float _Feather;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata v){ v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv,_MainTex); return o; }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 d = i.uv - _Center;
                float dist = length(d);
                float feather = max(1e-4, _Feather);
                float a = saturate(1.0 - smoothstep(_Radius - feather, _Radius, dist)) * _Opacity;

                fixed4 col = tex2D(_MainTex, i.uv) * _Tint;
                col.a *= a;
                return col;
            }
            ENDCG
        }
    }
    FallBack Off
}