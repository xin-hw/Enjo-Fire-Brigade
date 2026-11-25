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
                    return WriteOutlineMask(input.position, float3(.038, .62, .13), true);
                float3 albedo = tex2D(_MainTex, input.uv).rgb * _OutlineAlbedoTint.rgb;
                return WriteOutlineMask(input.position, DarkenOutlineColour(albedo), true);
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

            float Coverage(float4 value)
            {
                if (_FireRescueOutlineSamples <= 1) return value.a < 0 ? 1 : 0;
                float coveredSamples = floor(value.r * _FireRescueOutlineSamples + .002);
                return saturate(coveredSamples / _FireRescueOutlineSamples);
            }

            struct MaskSample
            {
                float4 value;
                float coverage, solidCoverage;
            };

            MaskSample ReadMask(float2 uv)
            {
                MaskSample sample;
                sample.value = tex2D(_MainTex, uv);
                sample.coverage = Coverage(sample.value);
                float samples = max(1, _FireRescueOutlineSamples);
                float solidCount = floor(sample.value.g * samples / SolidOutlineGreen + .002);
                sample.solidCoverage = min(sample.coverage, solidCount / samples);
                sample.value.g = max(0, sample.value.g - sample.solidCoverage * SolidOutlineGreen);
                if (_FireRescueOutlineSamples > 1)
                {
                    // Colour and depth were averaged with zero wherever a sample missed the model.
                    sample.value /= max(sample.coverage, .001);
                    sample.value.r = saturate((sample.value.r - 1) / OutlineRedScale);
                }
                return sample;
            }

            MaskSample StrokeSample(float2 uv, float opaqueDepth, float offsetInPixels, bool solidOnly, float effectDepth)
            {
                MaskSample sample = ReadMask(uv);
                float depth = -sample.value.a;
                if (solidOnly) sample.coverage = sample.solidCoverage;
                if (OutlineDepthVisible(depth, opaqueDepth, .00085) < .5 ||
                    (solidOnly && OutlineDepthVisible(depth, effectDepth, -.000025) < .5))
                {
                    sample.value = 0;
                    sample.coverage = 0;
                }
                if (sample.coverage > 0)
                {
                    // Estimate distance to the antialiased silhouette in screen pixels.
                    float distance = offsetInPixels + (.5 - sample.coverage) * .82;
                    sample.coverage = 1 - smoothstep(.78,1.56,distance);
                }
                return sample;
            }

            MaskSample StrongerStroke(MaskSample first, MaskSample second)
            {
                if (first.coverage > second.coverage) return first;
                if (second.coverage > first.coverage) return second;
                #if defined(UNITY_REVERSED_Z)
                    if (first.value.a <= second.value.a) return first;
                #else
                    if (first.value.a < 0 && (second.value.a >= 0 || first.value.a >= second.value.a)) return first;
                #endif
                return second;
            }

            bool TouchesExterior(float2 uv, float2 x, float2 y)
            {
                float side = min(min(Coverage(tex2D(_MainTex, uv - x)), Coverage(tex2D(_MainTex, uv + x))),
                                 min(Coverage(tex2D(_MainTex, uv - y)), Coverage(tex2D(_MainTex, uv + y))));
                if (side == 0) return true;
                float corner = min(min(Coverage(tex2D(_MainTex, uv - x - y)), Coverage(tex2D(_MainTex, uv + x + y))),
                                   min(Coverage(tex2D(_MainTex, uv - x + y)), Coverage(tex2D(_MainTex, uv + x - y))));
                return corner == 0;
            }

            MaskSample EdgeAt(float2 uv, float2 x, float2 y, float depth, bool solidOnly, float effectDepth)
            {
                MaskSample horizontal = StrongerStroke(StrokeSample(uv - x, depth, 1, solidOnly, effectDepth),
                                                       StrokeSample(uv + x, depth, 1, solidOnly, effectDepth));
                MaskSample vertical = StrongerStroke(StrokeSample(uv - y, depth, 1, solidOnly, effectDepth),
                                                     StrokeSample(uv + y, depth, 1, solidOnly, effectDepth));
                MaskSample edge = StrongerStroke(horizontal, vertical);
                if (edge.coverage >= 1) return edge;

                const float diagonal = 1.41421356;
                MaskSample slash = StrongerStroke(StrokeSample(uv - x - y, depth, diagonal, solidOnly, effectDepth),
                                                  StrokeSample(uv + x + y, depth, diagonal, solidOnly, effectDepth));
                MaskSample backslash = StrongerStroke(StrokeSample(uv - x + y, depth, diagonal, solidOnly, effectDepth),
                                                      StrokeSample(uv + x - y, depth, diagonal, solidOnly, effectDepth));
                return StrongerStroke(edge, StrongerStroke(slash, backslash));
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 pixel = abs(_MainTex_TexelSize.xy);
                float2 uv = input.pos.xy * pixel;
                MaskSample center = ReadMask(uv);
                bool overEffect = center.solidCoverage < center.coverage;
                if (center.coverage >= 1 && !overEffect) return 0;
                float2 x = float2(pixel.x, 0);
                float2 y = float2(0, pixel.y);
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                if (overEffect)
                {
                    // Effects behind a solid silhouette stay visible through its uncovered samples.
                    MaskSample foreground = EdgeAt(uv, x, y, depth, true, -center.value.a);
                    if (foreground.coverage > 0)
                        return fixed4(foreground.value.rgb, foreground.coverage * saturate((1 - center.solidCoverage) * 1.65));
                }
                if (center.coverage > 0)
                {
                    // Contact with the silhouette exterior distinguishes edges from interior mesh seams.
                    if (center.coverage >= 1 || !TouchesExterior(uv, x, y)) return 0;
                    return fixed4(center.value.rgb, saturate((1 - center.coverage) * 1.65));
                }
                MaskSample edge = EdgeAt(uv, x, y, depth, false, 0);
                return fixed4(edge.value.rgb, edge.coverage);
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
                float2 side = max(max(tex2D(_MainTex, uv - x).rg, tex2D(_MainTex, uv + x).rg),
                                  max(tex2D(_MainTex, uv - y).rg, tex2D(_MainTex, uv + y).rg));
                float2 corner = max(max(tex2D(_MainTex, uv - x - y).rg, tex2D(_MainTex, uv + x + y).rg),
                                    max(tex2D(_MainTex, uv - x + y).rg, tex2D(_MainTex, uv + x - y).rg));
                float2 edge = max(side, corner * .5);
                float3 colour = lerp(_FireRescueOccludedFireColor.rgb, float3(.16,.48,.92), step(.25, edge.g));
                return fixed4(colour, edge.r);
            }
            ENDCG
        }
        Pass
        {
            Name "RESOURCE_FOCUS_MASK"
            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask R
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "WorldOutlineMask.cginc"

            struct Interpolator { float4 position : SV_POSITION; };
            Interpolator vert(appdata_base input)
            {
                Interpolator output;
                output.position = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(Interpolator input) : SV_Target
            {
                float sceneDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, input.position.xy * _FireRescueOutlinePixelSize);
                clip(OutlineDepthVisible(input.position.z, sceneDepth, .00010) - .5);
                return 1;
            }
            ENDCG
        }
        Pass
        {
            Name "RESOURCE_FOCUS"
            Cull Off
            ZWrite Off
            ZTest Always
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _FireRescueResourceMask;
            float4 _MainTex_TexelSize;
            float _FireRescueResourceFocusStrength;

            float MaskAround(float2 uv, float2 radius)
            {
                float2 x = float2(radius.x, 0);
                float2 y = float2(0, radius.y);
                return max(max(tex2D(_FireRescueResourceMask, uv - x).r, tex2D(_FireRescueResourceMask, uv + x).r),
                           max(tex2D(_FireRescueResourceMask, uv - y).r, tex2D(_FireRescueResourceMask, uv + y).r));
            }

            half4 frag(v2f_img input) : SV_Target
            {
                half4 scene = tex2D(_MainTex, input.uv);
                float luminance = dot(scene.rgb, float3(.2126, .7152, .0722));
                float2 pixel = abs(_MainTex_TexelSize.xy);
                float2 uv = input.pos.xy * pixel;
                float body = tex2D(_FireRescueResourceMask, uv).r;
                float rim = saturate(MaskAround(uv, pixel * 1.4) - body);
                float glow = (MaskAround(uv, pixel * 3) * .25 + MaskAround(uv, pixel * 5) * .12) * (1 - body);
                float3 gold = lerp(float3(.55, .26, .035), float3(1, .84, .36), smoothstep(.07, .87, luminance));
                float3 graded = lerp(luminance.xxx, float3(1, .69, .17), saturate(rim * .86 + glow));
                graded = lerp(graded, gold, body);
                scene.rgb = lerp(scene.rgb, graded, _FireRescueResourceFocusStrength);
                return scene;
            }
            ENDCG
        }
    }
}
