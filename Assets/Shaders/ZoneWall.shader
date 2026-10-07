// Energy wall of the hill zone: a faint additive curtain, bright at the ground and fading upwards, almost clear when seen
// face-on and brighter edge-on, with thin lines rising up it, a slow shimmer running around and sparks drifting up.
// UV: x = metres around (a whole multiple of 4, so 1, 2 and 4 m patterns close the circle), y = metres above the ground.
// _Height is set by the map generator; _Color, _Danger and _Boost by HillZone.cs.
Shader "ProjectGame/ZoneWall"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.25,0.95,1,1)
        _Height ("Wall Height (m)", Float) = 2.4
        _Intensity ("Intensity", Range(0,3)) = 1
        _Danger ("Danger (set by HillZone)", Range(0,1)) = 0
        _Boost ("Extra animation time (set by HillZone)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off Cull Off Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Color;
            float _Height, _Intensity, _Danger, _Boost;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wpos : TEXCOORD1; float3 wn : TEXCOORD2; UNITY_FOG_COORDS(3) };

            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy;
                o.wpos = mul(unity_ObjectToWorld, v.vertex).xyz; o.wn = UnityObjectToWorldNormal(v.normal);
                UNITY_TRANSFER_FOG(o, o.pos); return o;
            }

            float band (float a, float lo, float hi, float w) { return saturate((a - lo) / w + 0.5) * saturate((hi - a) / w + 0.5); }
            float hash21 (float2 p) { float3 q = frac(float3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y + _Boost;
                float y = i.uv.y, h = saturate(y / _Height), fade = (1.0 - h) * (1.0 - h);
                float3 toCam = _WorldSpaceCameraPos - i.wpos, V = normalize(toCam);
                float rim = 1.0 - abs(dot(normalize(i.wn), V));                      // 0 face-on, 1 edge-on
                float curtain = lerp(0.22, 1.0, rim * rim);

                const float TAU = 6.2832;
                float shimmer = 0.7 + 0.3 * sin(TAU * (i.uv.x * 0.25 + t * 0.06)) * sin(TAU * (i.uv.x * 0.5 - t * 0.1));
                float ly = y * 1.6 - t * 0.4;                                         // thin lines rising
                float lines = band(frac(ly), 0.0, 0.07, max(fwidth(ly), 1e-4));
                float seam = exp(-max(y, 0.0) * 10.0);                                // where the wall meets the ground

                // sparks: one chance per 0.5 x 0.5 m cell, each column drifting up at its own speed
                float col = floor(i.uv.x * 2.0);
                float2 g = float2(i.uv.x * 2.0, y * 2.0 - t * (0.3 + 0.35 * hash21(float2(col, 7.0))));
                float2 cell = floor(g), f = frac(g) - 0.5;
                float2 off = (float2(hash21(cell + 17.0), hash21(cell + 31.0)) - 0.5) * 0.6;
                float d = length(f - off), wd = max(fwidth(d), 1e-4);
                float spark = step(0.74, hash21(cell)) * saturate((0.05 - d) / wd + 0.5);
                spark *= (1.0 - h) * (0.55 + 0.45 * sin(t * 3.0 + hash21(cell + 5.0) * TAU));
                spark *= smoothstep(0.6, 2.5, length(toCam));                       // no big blobs right in front of the eyes

                float k = fade * curtain * (0.2 * shimmer + 0.32 * lines) + seam * 0.5 + spark * 0.8;
                k *= _Intensity * (1.0 + 0.35 * _Danger);
                fixed4 c = fixed4(_Color.rgb * k, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, c, fixed4(0,0,0,0));               // additive: fog fades it out
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
