// Floor of the hill zone, drawn additively on a disc that follows the ground: a crisp line on the boundary with a soft glow
// on both sides of it, dashes marching around just inside the line, slow ripples running out from the centre, a faint fill.
// UV: x = metres around the boundary (a whole multiple of 4, so 1, 2 and 4 m patterns close the circle), y = metres from the centre.
// _Radius is set by the map generator; _Color, _Danger and _Boost by HillZone.cs.
Shader "ProjectGame/ZoneRing"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.25,0.95,1,1)
        _Radius ("Zone Radius (m)", Float) = 16
        _Intensity ("Intensity", Range(0,3)) = 1
        _Danger ("Danger (set by HillZone)", Range(0,1)) = 0
        _Boost ("Extra animation time (set by HillZone)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off Cull Off Lighting Off
        Offset -1, -1

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            half4 _Color;
            float _Radius, _Intensity, _Danger, _Boost;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };

            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy;
                UNITY_TRANSFER_FOG(o, o.pos); return o;
            }

            // 1 for a in [lo, hi], edges softened over w (one pixel of the coordinate)
            float band (float a, float lo, float hi, float w) { return saturate((a - lo) / w + 0.5) * saturate((hi - a) / w + 0.5); }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y + _Boost;
                float r = i.uv.y, e = _Radius - r;                                   // e: metres inside the boundary
                float we = max(fwidth(e), 1e-4), wx = max(fwidth(i.uv.x), 1e-4);

                float line_ = band(e, 0.0, 0.11, we);
                float glow = e > 0.0 ? exp(-e * 2.2) : exp(e * 7.0);                  // longer inwards, short outwards
                float dx = i.uv.x * 0.5 - t * 0.16;                                   // 2 m dashes marching around
                float dash = band(frac(dx), 0.0, 0.5, wx * 0.5) * band(e, 0.32, 0.46, we);
                float p = frac(t / 5.0);                                              // a ripple every 5 s, centre -> boundary
                float ripple = exp(-abs(r - p * _Radius) * 3.0) * smoothstep(0.0, 0.12, p) * (1.0 - 0.6 * p) * step(0.0, e);
                float rr = r / _Radius;
                float fill = step(0.0, e) * (0.005 + 0.014 * rr * rr);              // barely there: the glow is lit at night too

                float k = line_ * 1.1 + glow * 0.24 + dash * 0.45 + ripple * 0.08 + fill;
                k *= _Intensity * (1.0 + 0.35 * _Danger);
                fixed4 col = fixed4(_Color.rgb * k, 1);
                UNITY_APPLY_FOG_COLOR(i.fogCoord, col, fixed4(0,0,0,0));             // additive: fog fades it out
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
