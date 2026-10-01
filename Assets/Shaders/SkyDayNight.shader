// Skybox for the day / night cycle: gradient sky, sun disc with glow, moon, twinkling stars, drifting clouds.
// All colours and the sun / moon directions are driven by DayNightCycle.cs.
Shader "ProjectGame/SkyDayNight"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.20,0.42,0.85,1)
        _HorizonColor ("Horizon", Color) = (0.70,0.82,0.95,1)
        _GroundColor ("Below Horizon", Color) = (0.55,0.62,0.68,1)
        _HorizonPower ("Horizon Falloff", Range(0.1,2)) = 0.45
        _SunColor ("Sun Color", Color) = (1,0.95,0.85,1)
        _SunDir ("Sun Direction (to sun)", Vector) = (0.3,0.6,0.3,0)
        _SunSize ("Sun Size", Range(0.005,0.1)) = 0.028
        _MoonColor ("Moon Color", Color) = (0.85,0.9,1,1)
        _MoonDir ("Moon Direction (to moon)", Vector) = (-0.3,0.6,-0.3,0)
        _MoonSize ("Moon Size", Range(0.005,0.1)) = 0.022
        _StarIntensity ("Star Intensity", Range(0,2)) = 0
        _StarDensity ("Star Density", Range(40,300)) = 150
        _CloudColor ("Cloud Color", Color) = (1,1,1,1)
        _CloudCoverage ("Cloud Coverage", Range(0,1)) = 0.36
        _CloudSpeed ("Cloud Speed", Range(0,0.2)) = 0.012
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _MoonColor, _CloudColor;
            float4 _SunDir, _MoonDir;
            float _HorizonPower, _SunSize, _MoonSize, _StarIntensity, _StarDensity, _CloudCoverage, _CloudSpeed;

            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata_base v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.dir = v.vertex.xyz; return o;
            }

            float hash31 (float3 p) { p = frac(p * 0.1031); p += dot(p, p.yzx + 33.33); return frac((p.x + p.y) * p.z); }
            float hash21 (float2 p) { float3 q = frac(float3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1,0)), f.x), lerp(hash21(i + float2(0,1)), hash21(i + float2(1,1)), f.x), f.y);
            }
            float fbm (float2 p) { return vnoise(p) * 0.5 + vnoise(p * 2.03 + 7.1) * 0.25 + vnoise(p * 4.1 + 3.7) * 0.125 + vnoise(p * 8.3 + 1.3) * 0.0625; }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(h), _HorizonPower));
                col = lerp(col, _GroundColor.rgb, saturate(-h * 8.0));

                // stars: one random point per cell of a grid laid over the direction
                float3 sp = d * _StarDensity; float3 cell = floor(sp);
                float rnd = hash31(cell);
                float3 off = float3(hash31(cell + 11.0), hash31(cell + 23.0), hash31(cell + 37.0)) - 0.5;
                float star = step(0.986, rnd) * smoothstep(0.30, 0.0, length(frac(sp) - 0.5 - off * 0.5));
                col += star * (0.65 + 0.35 * sin(_Time.y * 3.0 + rnd * 60.0)) * _StarIntensity * saturate(h * 5.0) * float3(0.9, 0.95, 1.0);

                // moon
                float3 md = normalize(_MoonDir.xyz); float mdot = dot(d, md);
                float moon = smoothstep(cos(_MoonSize), cos(_MoonSize * 0.8), mdot);
                col = lerp(col, _MoonColor.rgb, moon * saturate(md.y * 8.0 + 0.5) * _MoonColor.a);
                col += _MoonColor.rgb * pow(saturate(mdot), 300.0) * 0.25 * _MoonColor.a;

                // sun
                float3 sd = normalize(_SunDir.xyz); float sdot = dot(d, sd);
                float vis = saturate(sd.y * 8.0 + 0.8);
                col += _SunColor.rgb * (pow(saturate(sdot), 90.0) * 0.55 + pow(saturate(sdot), 7.0) * 0.20) * vis;
                col += _SunColor.rgb * smoothstep(cos(_SunSize), cos(_SunSize * 0.8), sdot) * 5.0 * vis;

                // clouds: noise on a plane high above, fading into the horizon
                float2 uv = d.xz / (abs(h) + 0.12) * 0.9 + _Time.y * _CloudSpeed * float2(1.0, 0.35);
                float n = fbm(uv);
                float cloud = smoothstep(1.0 - _CloudCoverage, 1.0 - _CloudCoverage + 0.30, n + 0.15) * saturate(h * 6.0);
                float3 ccol = _CloudColor.rgb * (0.78 + 0.22 * fbm(uv * 2.7 + 9.0)) + _SunColor.rgb * pow(saturate(sdot), 5.0) * 0.35 * vis;
                col = lerp(col, ccol, cloud * 0.82);

                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
