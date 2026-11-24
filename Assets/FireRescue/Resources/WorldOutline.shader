Shader "FireRescue/WorldOutline"
{
    Properties
    {
        [HideInInspector] _MainTex ("Albedo or silhouette mask", 2D) = "white" {}
        [HideInInspector] _OutlineAlbedoTint ("Material tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _Selected ("Use selection colour", Float) = 0
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "WorldOutlineMask.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _OutlineAlbedoTint;
            float _Selected;
            struct Interpolator
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Interpolator vert(appdata_base input)
            {
                Interpolator output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                return output;
            }

            float4 frag(Interpolator input) : SV_Target
            {
                if (_Selected > .5)
                    return WriteOutlineMask(input.position, float3(.038, .62, .13));
                float3 albedo = tex2D(_MainTex, input.uv).rgb * _OutlineAlbedoTint.rgb;
                return WriteOutlineMask(input.position, DarkenOutlineColour(albedo));
            }
            ENDCG
        }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "WorldOutlineMask.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float4 Neighbour(float2 uv, float opaqueDepth)
            {
                float4 sample = tex2D(_MainTex, uv);
                if (sample.a >= 0 || OutlineDepthVisible(-sample.a, opaqueDepth, .00085) < .5) return 0;
                return sample;
            }

            float4 Frontmost(float4 first, float4 second)
            {
                #if defined(UNITY_REVERSED_Z)
                    return first.a <= second.a ? first : second;
                #else
                    return first.a < 0 && (second.a >= 0 || first.a >= second.a) ? first : second;
                #endif
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 pixel = abs(_MainTex_TexelSize.xy);
                float2 uv = input.pos.xy * pixel;
                if (tex2D(_MainTex, uv).a < 0) return 0;

                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                float2 x = float2(pixel.x, 0);
                float2 y = float2(0, pixel.y);
                float4 edge = Frontmost(Frontmost(Neighbour(uv - x, depth), Neighbour(uv + x, depth)),
                                        Frontmost(Neighbour(uv - y, depth), Neighbour(uv + y, depth)));
                if (edge.a < 0) return fixed4(edge.rgb, 1);

                // Diagonal corner pixels soften the one-pixel contour.
                edge = Frontmost(Frontmost(Neighbour(uv - x - y, depth), Neighbour(uv + x + y, depth)),
                                 Frontmost(Neighbour(uv - x + y, depth), Neighbour(uv + x - y, depth)));
                return fixed4(edge.rgb, edge.a < 0 ? .5 : 0);
            }
            ENDCG
        }

        Pass
        {
            Name "WALL_FIRE_OUTLINE"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _FireRescueWallMask;
            float4 _MainTex_TexelSize;
            fixed4 _FireRescueOccludedFireColor;

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 pixel = abs(_MainTex_TexelSize.xy);
                float2 uv = input.pos.xy * pixel;
                // Keep the centre transparent and constrain the line to the visible wall.
                clip(tex2D(_FireRescueWallMask, uv).r - .5);
                clip(.5 - tex2D(_MainTex, uv).r);

                float2 x = float2(pixel.x, 0);
                float2 y = float2(0, pixel.y);
                float side = max(max(tex2D(_MainTex, uv - x).r, tex2D(_MainTex, uv + x).r),
                                 max(tex2D(_MainTex, uv - y).r, tex2D(_MainTex, uv + y).r));
                float corner = max(max(tex2D(_MainTex, uv - x - y).r, tex2D(_MainTex, uv + x + y).r),
                                   max(tex2D(_MainTex, uv - x + y).r, tex2D(_MainTex, uv + x - y).r));
                return fixed4(_FireRescueOccludedFireColor.rgb, max(side, corner * .5));
            }
            ENDCG
        }
    }
}
