// Alpha-blended dust and smoke (impact clouds, the cannonball trail, muzzle smoke): texture alpha x vertex colour, lit by the
// ambient light and a little of the sun, so a dust cloud is grey by day and dark at night (an unlit one would glow).
Shader "ProjectGame/DustParticle"
{
    Properties
    {
        _MainTex ("Texture (alpha)", 2D) = "white" {}
        _Color ("Tint", Color) = (0.62, 0.56, 0.48, 1)
        _SunShare ("Sun share", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color; half _SunShare;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; UNITY_FOG_COORDS(1) };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                half3 light = ShadeSH9(half4(0, 1, 0, 1)) + _LightColor0.rgb * _SunShare;
                o.color = v.color * _Color * half4(light, 1);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }
}
