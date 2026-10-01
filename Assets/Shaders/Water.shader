// Lake water: two scrolling normal layers, fresnel blend between the deep colour and the sky,
// sun / moon glint. Sky colours come from the globals set by DayNightCycle.cs (_PG_SkyZenith, _PG_SkyHorizon).
Shader "ProjectGame/Water"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.04,0.16,0.20,1)
        _NormalTex ("Normal Map", 2D) = "bump" {}
        _Tiling ("Tiling (1/m)", Range(0.01,1)) = 0.12
        _Strength ("Wave Strength", Range(0,2)) = 0.55
        _Speed ("Wave Speed", Range(0,1)) = 0.06
        _Gloss ("Glint Sharpness", Range(20,600)) = 260
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            fixed4 _DeepColor;
            sampler2D _NormalTex;
            half _Tiling, _Strength, _Speed, _Gloss;
            fixed4 _PG_SkyZenith, _PG_SkyHorizon;

            struct v2f { float4 pos : SV_POSITION; float3 wpos : TEXCOORD0; UNITY_FOG_COORDS(1) };

            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
                UNITY_TRANSFER_FOG(o, o.pos); return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.wpos.xz * _Tiling; float t = _Time.y * _Speed;
                float3 n1 = UnpackNormal(tex2D(_NormalTex, uv + float2(t, t * 0.6)));
                float3 n2 = UnpackNormal(tex2D(_NormalTex, uv * 1.7 + float2(-t * 0.8, t * 0.9)));
                float3 n = normalize(float3((n1.x + n2.x) * _Strength, 1.0, (n1.y + n2.y) * _Strength));
                float3 v = normalize(_WorldSpaceCameraPos - i.wpos);
                float fres = 0.04 + 0.96 * pow(1.0 - saturate(dot(v, n)), 4.0);
                float3 r = reflect(-v, n);
                float3 sky = lerp(_PG_SkyHorizon.rgb, _PG_SkyZenith.rgb, pow(saturate(r.y), 0.6));
                float3 col = lerp(_DeepColor.rgb * (0.35 + 0.65 * saturate(_PG_SkyHorizon.g * 1.4)), sky, fres);
                float3 l = normalize(_WorldSpaceLightPos0.xyz);
                col += _LightColor0.rgb * pow(saturate(dot(n, normalize(l + v))), _Gloss) * 2.5;
                fixed4 o = fixed4(col, lerp(0.78, 0.98, fres));
                UNITY_APPLY_FOG(i.fogCoord, o);
                return o;
            }
            ENDCG
        }
    }
    Fallback Off
}
