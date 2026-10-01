// Camera effect: the picture blurs and darkens towards the sides of the screen (the centre stays sharp),
// plus a slight saturation boost and a red pulse at the edges when the player is hurt. Used by EdgeBlurEffect.cs.
Shader "Hidden/ProjectGame/EdgeBlur"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BlurStart, _BlurEnd, _BlurSize, _Vignette, _Saturation, _Damage, _SideBias;
            fixed4 _DamageColor;

            static const float2 TAPS[12] = {
                float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
                float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
                float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598) };

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                p.y *= _SideBias;                                     // < 1: the effect is stronger at the left and right sides
                float mask = smoothstep(_BlurStart, _BlurEnd, length(p));
                float2 r = _MainTex_TexelSize.xy * _BlurSize * mask * (_ScreenParams.y / 1080.0);
                float3 col = tex2D(_MainTex, i.uv).rgb;
                if (mask > 0.001)
                {
                    for (int k = 0; k < 12; k++) col += tex2D(_MainTex, i.uv + TAPS[k] * r).rgb;
                    col /= 13.0;
                }
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(float3(luma, luma, luma), col, _Saturation);
                col *= 1.0 - _Vignette * mask * mask;
                col = lerp(col, _DamageColor.rgb, saturate(_Damage * (0.15 + mask) * 0.6));
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
