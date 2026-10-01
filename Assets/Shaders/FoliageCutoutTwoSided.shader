// Leaf cards: alpha cutout, rendered from both sides, lit with the mesh's (spherical) normals on both faces,
// so the crown shades as one soft volume. Every tree gets its own tint from its world position,
// and leaves glow when you look through them towards the sun (cheap translucency).
// Casts and receives shadows, supports GPU instancing and fog.
Shader "ProjectGame/FoliageCutoutTwoSided"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _TintA ("Tree Tint A", Color) = (0.70,0.80,0.62,1)
        _TintB ("Tree Tint B", Color) = (1.08,1.02,0.80,1)
        _MainTex ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Translucency ("Back Light", Range(0,1)) = 0.35
        _WindStrength ("Wind Strength (m)", Range(0,0.5)) = 0.05
        _WindSpeed ("Wind Speed", Range(0,5)) = 1.2
    }
    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        LOD 200
        Cull Off

        CGPROGRAM
        #pragma surface surf Lambert alphatest:_Cutoff addshadow fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        fixed4 _TintA;
        fixed4 _TintB;
        half _Translucency;
        float _WindStrength;
        float _WindSpeed;

        struct Input
        {
            float2 uv_MainTex;
            float treeTint;
            float3 worldPos;
        };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            float3 origin = float3(unity_ObjectToWorld[0].w, unity_ObjectToWorld[1].w, unity_ObjectToWorld[2].w);
            o.treeTint = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453);

            float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
            float phase = wp.x * 0.31 + wp.z * 0.27;
            float h = saturate(v.vertex.y / 6.0);
            float t = _Time.y * _WindSpeed;
            v.vertex.x += sin(t + phase) * _WindStrength * h * h;
            v.vertex.z += cos(t * 0.7 + phase * 1.3) * _WindStrength * 0.6 * h * h;
        }

        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb * lerp(_TintA.rgb, _TintB.rgb, IN.treeTint);
            o.Alpha = c.a;
            float3 v = normalize(_WorldSpaceCameraPos - IN.worldPos);
            float3 l = normalize(_WorldSpaceLightPos0.xyz);
            o.Emission = o.Albedo * _LightColor0.rgb * pow(saturate(dot(-v, l)), 4.0) * _Translucency;
        }
        ENDCG
    }
    FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
