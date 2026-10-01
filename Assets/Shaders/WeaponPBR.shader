// Weapon models exported from Substance-style texture sets: separate metallic, roughness and AO maps
// (the Standard shader wants metallic + smoothness packed into one texture).
Shader "ProjectGame/WeaponPBR"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _MainTex ("Base Color", 2D) = "white" {}
        [Normal] _BumpMap ("Normal (OpenGL)", 2D) = "bump" {}
        _MetallicMap ("Metallic (R)", 2D) = "black" {}
        _RoughnessMap ("Roughness (R)", 2D) = "white" {}
        _OcclusionMap ("Ambient Occlusion (R)", 2D) = "white" {}
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _MetallicScale ("Metallic Scale", Range(0,1)) = 1
        _SmoothnessScale ("Smoothness Scale", Range(0,1.5)) = 1
        _OcclusionStrength ("AO Strength", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex, _BumpMap, _MetallicMap, _RoughnessMap, _OcclusionMap, _EmissionMap;
        fixed4 _Color, _EmissionColor;
        half _MetallicScale, _SmoothnessScale, _OcclusionStrength;

        struct Input { float2 uv_MainTex; };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.uv_MainTex;
            fixed4 c = tex2D(_MainTex, uv) * _Color;
            o.Albedo = c.rgb;
            o.Normal = UnpackNormal(tex2D(_BumpMap, uv));
            o.Metallic = tex2D(_MetallicMap, uv).r * _MetallicScale;
            o.Smoothness = saturate((1.0 - tex2D(_RoughnessMap, uv).r) * _SmoothnessScale);
            o.Occlusion = lerp(1.0, tex2D(_OcclusionMap, uv).r, _OcclusionStrength);
            o.Emission = tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Standard"
}
