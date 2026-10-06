// Dust patch left on the ground by a cannon hit: a mesh laid over the terrain, lit like the ground, faded out by _Fade.
// Drawn after the terrain and before the grass (the grass stands on top of the dust), with a depth offset against flicker.
Shader "ProjectGame/DustDecal"
{
    Properties
    {
        _MainTex ("Dust (alpha)", 2D) = "white" {}
        _Color ("Tint", Color) = (0.55, 0.49, 0.40, 1)
        _Fade ("Fade", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Geometry+100" "RenderType"="Transparent" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" }
        ZWrite Off
        Offset -1, -2

        CGPROGRAM
        #pragma surface surf Lambert alpha:fade noshadow nolightmap nodynlightmap nometa
        sampler2D _MainTex; fixed4 _Color; half _Fade;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            fixed4 t = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = _Color.rgb * t.rgb;
            o.Alpha = t.a * _Color.a * _Fade;
        }
        ENDCG
    }
}
