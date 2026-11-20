Shader "FireRescue/WaterLab"
{
    Properties
    {
        _Surface ("Show surface film", Float) = 0
        _WaterData ("Depth and footprint", 2D) = "black" {}
        _Grid ("Grid size and inverse size", Vector) = (1,1,1,1)
        [HideInInspector] _WaterTime ("Elapsed simulation time", Float) = 0
        [HideInInspector] _RippleActive ("Recent surface contact", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_FireRescueClearWaterScene" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _WaterData;
            sampler2D _FireRescueClearWaterScene;
            float4 _FireRescueClearWaterScene_TexelSize;
            float4 _Grid;
            float _Surface, _WaterTime, _RippleActive;
            float4 _Ripples[8];

            struct Vertex
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Fragment
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float4 grab : TEXCOORD2;
                float4 color : COLOR;
            };
            Fragment vert(Vertex v)
            {
                Fragment o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.grab = ComputeGrabScreenPos(o.position);
                o.color = v.color;
                return o;
            }
            float hash(float2 position)
            {
                float3 q = frac(position.xyx * .1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }

            float noise(float2 position)
            {
                float2 root = floor(position), blend = frac(position);
                blend = blend * blend * (3 - 2 * blend);
                return lerp(lerp(hash(root), hash(root + float2(1,0)), blend.x),
                    lerp(hash(root + float2(0,1)), hash(root + 1), blend.x), blend.y);
            }

            float refractionDifference(Fragment i, float2 offsetInPixels, out float sceneLevel)
            {
                float2 uv = i.grab.xy / i.grab.w;
                float3 behind = tex2D(_FireRescueClearWaterScene, uv).rgb;
                float2 offset = offsetInPixels * abs(_FireRescueClearWaterScene_TexelSize.xy);
                float3 bent = tex2D(_FireRescueClearWaterScene, uv + offset).rgb;
                // Use a neutral refraction contrast, keeping the actual framebuffer's
                // color as the base. Grab formats and HDR can differ from the target.
                float3 luminance = float3(.2126,.7152,.0722);
                sceneLevel = saturate(dot(behind,luminance));
                return dot(bent - behind,luminance);
            }

            fixed4 film(Fragment i)
            {
                float2 cell = (floor(min(i.uv, .99999) * _Grid.xy) + .5) * _Grid.zw;
                clip(tex2D(_WaterData, cell).g - .9);

                float2 p = i.world.xz;
                float broad = noise(p * 5.7);
                float detail = noise(p * 15.7 + 7);
                float2 drift = float2(broad - .5, noise(p * 6.2 + 24) - .5) * .64 * _Grid.zw;
                float2 uv = i.uv + drift;
                float center = tex2D(_WaterData, uv).r;
                float west = tex2D(_WaterData, uv - float2(.72,0) * _Grid.zw).r;
                float east = tex2D(_WaterData, uv + float2(.72,0) * _Grid.zw).r;
                float south = tex2D(_WaterData, uv - float2(0,.72) * _Grid.zw).r;
                float north = tex2D(_WaterData, uv + float2(0,.72) * _Grid.zw).r;
                float depth = center * .44 + (west + east + south + north) * .14;
                float waterline = depth - (.098 + (broad - .5) * .058 + (detail - .5) * .026);
                float aa = clamp(fwidth(waterline), .009, .045);
                float coverage = smoothstep(-aa, aa, waterline);
                clip(coverage - .018);
                float rim = 1 - smoothstep(.010, .083, abs(waterline - .019));
                float2 slope = float2(west - east, south - north);
                float facing = dot(slope, float2(.8,.6));
                float edgeLight = rim * smoothstep(.02,.22,facing) * (.54 + .46 * detail);
                float edgeShadow = rim * smoothstep(.02,.24,-facing);

                float ring = 0;
                if (_RippleActive > .5)
                {
                    [unroll] for (int j = 0; j < 8; j++)
                    {
                        float age = _WaterTime - _Ripples[j].z;
                        float radius = length(p - _Ripples[j].xy);
                        float crest = radius - age * (.53 + _Ripples[j].w * .24);
                        float angle = dot(normalize(p - _Ripples[j].xy + .001), float2(.85,-.53));
                        float envelope = smoothstep(0,.055,age) * (1 - smoothstep(.15,.72,age))
                            * lerp(.30,1,smoothstep(-.45,.7,angle + (broad - .5) * .45));
                        float shape = (1 - smoothstep(.010,.033,abs(crest)))
                            - .50 * (1 - smoothstep(.008,.026,abs(crest + .034)));
                        ring += shape * envelope * _Ripples[j].w;
                    }
                }

                ring = clamp(ring,-.24,.72);
                float sheen = smoothstep(.59,.86,noise(float2(p.x * 4 + p.y * 1.7, p.y * 15 - p.x * 3.8)))
                    * smoothstep(.16,.60,depth) * .048;
                float sceneLevel;
                float lens = clamp(refractionDifference(i, slope * 6.5 + ring * float2(1.8,-1.3), sceneLevel),-.20,.20);
                float reflection = .016 + depth * .061 + edgeShadow * .10;
                // Blend a thin neutral lens over the intact floor or countertop.
                float light = .052 * edgeLight + sheen * 1.15 + ring * .090;
                float tone = saturate(sceneLevel - reflection + light + lens * .39);
                return fixed4(tone.xxx, coverage * (.81 + depth * .15));
            }

            fixed4 drop(Fragment i)
            {
                float2 p = i.uv * 2 - 1;
                float shape = i.color.r;
                float phase = i.color.b * 6.2832;
                float center = (shape - .5) * .46 * (1 - p.y * p.y) + sin(p.y * 4.6 + phase) * .10;
                float bulge = sqrt(saturate(1 - p.y * p.y)) * (.71 + .20 * p.y + .13 * cos(p.y * 5.1 + phase));
                float across = (p.x - center) / max(bulge, .025);
                float edgeDistance = abs(across);
                float aa = clamp(fwidth(edgeDistance), .064, .30);
                float silhouette = 1 - smoothstep(1 - aa, 1 + aa, edgeDistance);
                silhouette *= smoothstep(0,.14,1 - abs(p.y));
                clip(silhouette - .02);

                float sideRim = smoothstep(.48,.92,edgeDistance);
                float shoulder = .62 + .38 * cos(p.y * 5.6 + phase);
                float glint = exp(-pow((across + .47) * 4.6, 2)) * shoulder;
                float spark = exp(-pow((across + .25) * 6, 2) - pow((p.y - .38 - shape * .28) * 3.4, 2));
                float sceneLevel;
                float lens = clamp(refractionDifference(i, float2(across, p.y * .58) * (1 + sideRim * 2.2), sceneLevel),-.34,.34);
                float darkness = .106 + sideRim * .24 + smoothstep(.02,1,across) * .075;
                float highlights = glint * .43 + spark * lerp(.35,.55,i.color.g);
                float tone = saturate(sceneLevel * (1 - darkness) + lens * .40 + highlights);
                return fixed4(tone.xxx, silhouette * i.color.a * .86);
            }

            fixed4 frag(Fragment i) : SV_Target
            {
                if (_Surface > .5) return film(i);
                return drop(i);
            }
            ENDCG
        }
    }
}
