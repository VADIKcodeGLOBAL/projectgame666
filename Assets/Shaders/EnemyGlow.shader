// Neon box enemy: dark body, glowing edges and a bright visor stripe on the front, rim light, hit flash.
// _EmissionColor and _Flash are set per bot through a MaterialPropertyBlock.
Shader "ProjectGame/EnemyGlow"
{
    Properties
    {
        _Color ("Body Color", Color) = (0.06,0.06,0.08,1)
        [HDR] _EmissionColor ("Glow Color", Color) = (1,0.35,0.1,1)
        _EdgeWidth ("Edge Width", Range(0.01,0.2)) = 0.07
        _Pulse ("Pulse Speed", Range(0,10)) = 4
        _Flash ("Hit Flash", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "DisableBatching"="True" }   // the glow pattern needs object-space positions
        LOD 200

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert addshadow
        #pragma target 3.0

        fixed4 _Color;
        half4 _EmissionColor;
        half _EdgeWidth, _Pulse, _Flash;

        struct Input
        {
            float3 localPos;
            float3 viewDir;
            float3 worldPos;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.localPos = v.vertex.xyz;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            float3 a = abs(IN.localPos);                         // unit cube: faces at 0.5
            float lo = 0.5 - _EdgeWidth;
            float ex = smoothstep(lo, 0.5, a.x), ey = smoothstep(lo, 0.5, a.y), ez = smoothstep(lo, 0.5, a.z);
            float edge = saturate(max(ex * ey, max(ey * ez, ex * ez)) * 1.6);
            float visor = step(0.49, IN.localPos.z) * smoothstep(0.20, 0.24, IN.localPos.y) * (1.0 - smoothstep(0.34, 0.38, IN.localPos.y)) * step(a.x, 0.36);
            float rim = pow(1.0 - saturate(dot(normalize(IN.viewDir), o.Normal)), 3.0);
            float pulse = 0.75 + 0.25 * sin(_Time.y * _Pulse + IN.worldPos.x * 0.7 + IN.worldPos.z * 0.9);
            o.Albedo = _Color.rgb;
            o.Emission = _EmissionColor.rgb * (edge * 1.3 + visor * 2.2 + rim * 0.45) * pulse + _Flash * float3(1.6, 1.6, 1.6);
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
