// Energy wall of the hill zone: additive, bright at the ground and fading upwards, with rising stripes.
// UV: x runs around the zone, y from the ground (0) to the top (1). _Color is set by HillZone.cs.
Shader "ProjectGame/ZoneWall"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.2,0.9,1,1)
        _Stripes ("Stripes", Range(1,40)) = 9
        _Speed ("Rise Speed", Range(0,3)) = 0.6
        _Intensity ("Intensity", Range(0,3)) = 0.7
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off Cull Off Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Color;
            half _Stripes, _Speed, _Intensity;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }

            fixed4 frag (v2f i) : SV_Target
            {
                float fade = pow(1.0 - saturate(i.uv.y), 3.0);
                float stripe = 0.6 + 0.4 * sin(i.uv.y * _Stripes - _Time.y * _Speed * 6.2832);
                float a = saturate(fade * stripe * 0.5) * _Intensity;
                return fixed4(_Color.rgb, a * _Color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
