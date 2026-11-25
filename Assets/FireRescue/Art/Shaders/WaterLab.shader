Shader "FireRescue/WaterLab"
{
    Properties
    {
        _Surface ("Show surface film", Float) = 0
        _WaterData ("Depth and footprint", 2D) = "black" {}
        _Grid ("Grid size and inverse size", Vector) = (1,1,1,1)
        [HideInInspector] _WaterTime ("Elapsed simulation time", Float) = 0
        [HideInInspector] _RippleActive ("Recent surface contact", Float) = 0
        [HideInInspector] _ColorBlocks ("Opaque color blocks", Float) = 0
        [HideInInspector] _PaintedDetail ("Painted water details", Float) = 0
        [HideInInspector] _SculptedWater ("Shaded water volume", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_FireRescueClearWaterScene" }
        CGINCLUDE
            #include "UnityCG.cginc"

            sampler2D _WaterData;
            sampler2D _FireRescueClearWaterScene;
            float4 _FireRescueClearWaterScene_TexelSize;
            float4 _Grid;
            float _Surface, _WaterTime, _RippleActive, _ColorBlocks, _PaintedDetail, _SculptedWater;
            float4 _Ripples[8];
            float4 _RippleFlow[8];
            static const float3 WaterLight = float3(.86,.96,.97);
            static const float3 WaterShallow = float3(.58,.83,.90);
            static const float3 WaterBody = float3(.41,.73,.83);
            static const float3 WaterShade = float3(.32,.63,.76);

            struct Vertex
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Fragment
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 world : TEXCOORD1;
                float4 grab : TEXCOORD2;
                float3 worldNormal : TEXCOORD3;
                float4 color : COLOR;
            };
            Fragment vert(Vertex v)
            {
                Fragment o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.grab = ComputeGrabScreenPos(o.position);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
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

            float3 standingWaterDetail(float3 fill, float2 world, float waterline, float2 slope,
                float broad, float strongestCrest)
            {
                float facing = dot(slope, float2(.8,.6)) / max(.06, length(slope));
                float bank = step(.021, waterline) * (1 - step(.075, waterline));
                fill = lerp(fill, WaterShade * .90, bank * step(.31, -facing) * .66);
                fill = lerp(fill, float3(.68,.875,.92), bank * step(.26, facing) * step(.33, broad));

                if (_SculptedWater > .5)
                {
                    // Follow a boundary of the gathered water so glints grow with the puddle.
                    float inset = 1 - smoothstep(.017,.045,abs(waterline - .093));
                    float broken = smoothstep(.32,.67,noise(world * 3.8 + float2(2,13)));
                    float lit = smoothstep(.02,.48,facing);
                    fill = lerp(fill, WaterLight, inset * broken * lit * .74);
                    float pool = smoothstep(.48,.77,noise(world * 1.85 + float2(13,7)))
                        * smoothstep(.14,.34,waterline);
                    fill = lerp(fill, WaterBody, pool * .54);
                    float2 reflectionAxes = float2(dot(world, float2(.91,.42)), dot(world, float2(-.42,.91)));
                    float reflection = smoothstep(.56,.69,noise(reflectionAxes * float2(1.55,5.8) + float2(7,2)))
                        * smoothstep(.14,.29,waterline);
                    fill = lerp(fill, WaterShallow, reflection * .56);
                }
                else
                {
                    float2 stroke = float2(dot(world, float2(.88,.48)), dot(world, float2(-.48,.88)));
                    float curl = sin(stroke.y * 13.2 + sin(stroke.x * 4.8) * .52);
                    float broken = noise(stroke * float2(3.6,3.4));
                    float reflection = smoothstep(.83,.96,curl) * smoothstep(.46,.64,broken) * step(.105, waterline);
                    fill = lerp(fill, float3(.67,.85,.91), reflection * .83);
                }

                // Use the strongest nearby impact wave so overlapping hits keep separated arcs.
                fill = lerp(fill, WaterShallow, smoothstep(.16,.25,strongestCrest));
                return lerp(fill, WaterLight, smoothstep(.31,.50,strongestCrest));
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
                float strongestCrest = 0;
                if (_RippleActive > .5)
                {
                    [unroll] for (int j = 0; j < 8; j++)
                    {
                        float age = _WaterTime - _Ripples[j].z;
                        float2 radial = p - _Ripples[j].xy;
                        float2 heading = normalize(radial + .001);
                        float radius = length(radial);
                        float angle = dot(heading, float2(.85,-.53));
                        float envelope = smoothstep(0,.055,age) * (1 - smoothstep(.15,.72,age))
                            * lerp(.30,1,smoothstep(-.45,.7,angle + (broad - .5) * .45));
                        if (_SculptedWater > .5)
                        {
                            // Shallow hits carry the crest forward and shade the shorter back arc.
                            float2 flow = _RippleFlow[j].xy;
                            float forward = dot(heading, flow);
                            radius = length(radial - flow * age * .14) / (1 + max(0,forward) * .34);
                            radius += (detail - .5) * .028;
                            envelope *= lerp(.23,.94,smoothstep(-.47,.62,forward + (broad - .5) * 1.12));
                        }
                        float crest = radius - age * (.53 + _Ripples[j].w * .24);
                        float shape = (1 - smoothstep(.010,.033,abs(crest)))
                            - .50 * (1 - smoothstep(.008,.026,abs(crest + .034)));
                        float crestStrength = shape * envelope * _Ripples[j].w;
                        ring += crestStrength;
                        strongestCrest = max(strongestCrest, crestStrength);
                    }
                }

                ring = clamp(ring,-.24,.72);
                float sheen = smoothstep(.59,.86,noise(float2(p.x * 4 + p.y * 1.7, p.y * 15 - p.x * 3.8)))
                    * smoothstep(.16,.60,depth) * .048;
                if (_ColorBlocks > .5)
                {
                    clip(coverage - .46);
                    // Thickness from the water simulation sets the colour regions.
                    float3 blocks = lerp(WaterShallow, WaterBody, step(.21, depth));
                    blocks = lerp(blocks, WaterShade, step(.46, depth));
                    if (_PaintedDetail > .5)
                        return fixed4(standingWaterDetail(blocks, p, waterline, slope, broad, strongestCrest), 1);
                    float glint = max(step(.032, sheen), step(.18, ring));
                    return fixed4(lerp(blocks, WaterLight, glint), 1);
                }

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
                if (_ColorBlocks > .5)
                {
                    clip(silhouette - .40);
                    float3 blocks = lerp(WaterBody, WaterShade, step(.48, across));
                    if (_PaintedDetail > .5)
                    {
                        float crescent = across + .35 - .12 * sin(p.y * 3.1 + phase);
                        float taper = smoothstep(-.65,-.34,p.y) * (1 - smoothstep(.48,.84,p.y));
                        float band = (1 - smoothstep(.12,.27,abs(crescent))) * taper;
                        blocks = lerp(blocks, WaterShallow, smoothstep(.23,.66,band));
                        float white = smoothstep(.52,.80,glint * taper + .22 * spark);
                        return fixed4(lerp(blocks, WaterLight, white * lerp(.75, 1, i.color.g)), 1);
                    }
                    blocks = lerp(blocks, WaterShallow, step(.30, glint));
                    return fixed4(lerp(blocks, WaterLight, step(.62, glint + .24 * spark)), 1);
                }

                float sceneLevel;
                float lens = clamp(refractionDifference(i, float2(across, p.y * .58) * (1 + sideRim * 2.2), sceneLevel),-.34,.34);
                float darkness = .106 + sideRim * .24 + smoothstep(.02,1,across) * .075;
                float highlights = glint * .43 + spark * lerp(.35,.55,i.color.g);
                float tone = saturate(sceneLevel * (1 - darkness) + lens * .40 + highlights);
                return fixed4(tone.xxx, silhouette * i.color.a * .86);
            }

            fixed4 sculptedDrop(Fragment i)
            {
                float3 normal = normalize(i.worldNormal);
                float light = dot(normal, float3(.33,.82,-.47));
                float3 fill = lerp(WaterShade, WaterBody, smoothstep(-.48,-.17,light));
                fill = lerp(fill, WaterShallow, smoothstep(.32,.62,light));
                // The short painted stroke remains anchored to the drop as it turns.
                float bend = .14 * dot(normal, UNITY_MATRIX_V[0].xyz);
                float offset = (i.color.r - .5) * .21;
                float stroke = 1 - smoothstep(.15,.30,abs(i.uv.y - .54 + bend + offset));
                float highlight = lerp(.21,.72,smoothstep(.28,.87,i.color.b));
                float glint = smoothstep(.68,.91,light) * stroke * max(highlight, i.color.g * .80);
                return fixed4(lerp(fill, WaterLight, glint), 1);
            }

            fixed4 frag(Fragment i) : SV_Target
            {
                if (_Surface > .5) return film(i);
                if (_SculptedWater > .5) return sculptedDrop(i);
                return drop(i);
            }
        ENDCG

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ColorBlocks]
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            ENDCG
        }
        Pass
        {
            Name "OUTLINE_MASK"
            Tags { "LightMode"="FireRescueOutline" }
            Cull Off
            ZWrite On
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment maskFragment
            #include "Assets/FireRescue/Resources/WorldOutlineMask.cginc"

            float4 maskFragment(Fragment input) : SV_Target
            {
                return WriteEffectOutlineMask(input.position, frag(input).a, WaterShade);
            }
            ENDCG
        }
    }
}
