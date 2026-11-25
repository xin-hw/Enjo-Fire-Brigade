Shader "FireRescue/FlowingFire"
{
    Properties
    {
        _OuterColor ("Outer flame", Color) = (0.86,0.12,0.008,1)
        _CoreColor ("Small bright core", Color) = (1,0.72,0.055,1)
        _FireTime ("Local presentation clock", Float) = 0
        _Phase ("Stable tongue phase", Float) = 0
        _CoreBoost ("Inner tongue boost", Range(0,1)) = 0
        _EdgeMask ("Exposed cell edges L R B F", Vector) = (1,1,1,1)
        [HideInInspector] _HeatMap ("Live floor coverage", 2D) = "black" {}
        [HideInInspector] _HeatMapWorld ("Floor location and size", Vector) = (0,0,1,1)
        [HideInInspector] _ColorBlocks ("Opaque color blocks", Float) = 0
        [HideInInspector] _PaintedDetail ("Painted flame details", Float) = 0
        [HideInInspector] _FirePalette ("Fire class palette", Float) = 0
        [HideInInspector] _FireBend ("Airflow at flame tip", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        CGINCLUDE
            #include "UnityCG.cginc"
            float4 _OuterColor, _CoreColor, _EdgeMask;
            sampler2D _HeatMap;
            float4 _HeatMapWorld;
            float _FireTime, _Phase, _CoreBoost, _ColorBlocks, _PaintedDetail, _FirePalette;
            float4 _FireBend;
            static const float3 FlameOuter = float3(.91,.25,.12);
            float3 flameRim()
            {
                if (_FirePalette > 2.5) return float3(.07,.29,.79);
                if (_FirePalette > 1.5) return float3(.96,.38,.07);
                if (_FirePalette > .5) return float3(.83,.13,.115);
                return FlameOuter;
            }
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; float4 uv:TEXCOORD0; float3 normal:TEXCOORD1; float2 worldXZ:TEXCOORD2; };
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(cell),hash(cell+float2(1,0)),f.x),
                    lerp(hash(cell+float2(0,1)),hash(cell+1),f.x),f.y);
            }

            fixed4 flamePixel(float3 colour, float alpha, float heat)
            {
                if (_ColorBlocks <= .5) return fixed4(colour, alpha);

                clip(alpha - .28);
                float3 blocks = lerp(FlameOuter, float3(1,.52,.14), step(.29, heat));
                blocks = lerp(blocks, float3(1,.81,.34), step(.72, heat));
                return fixed4(blocks, 1);
            }

            float ashChips(float2 tiles)
            {
                float2 cell = floor(tiles);
                float seed = hash(cell + float2(23, 17));
                float2 p = frac(tiles) - lerp(.31, .69, float2(seed, hash(cell + 7)));
                p.x += p.y * (seed - .5) * 1.6;
                float shape = max(abs(p.x + p.y * .56) + abs(p.y) * .8, abs(p.y) * 2);
                float size = lerp(.13, .29, hash(cell + 31));
                return (1 - smoothstep(size - .025, size + .035, shape)) * step(.35, seed);
            }

            fixed4 groundAsh(float2 world)
            {
                float2 source=(world-_HeatMapWorld.xy)*_HeatMapWorld.zw;
                clip(min(source.x,source.y));
                clip(1-max(source.x,source.y));
                float broad = noise(world * 3.9);
                float2 bend = float2(broad - .5, noise(world * 4.1 + float2(13,6)) - .5) * .16;
                float2 map=(world+bend-_HeatMapWorld.xy)*_HeatMapWorld.zw;
                float2 heat=tex2D(_HeatMap,map).rg;
                // Dust follows the real fire footprint. Fixed chips and shaded
                // patches give it a matte, irregular surface at the game camera.
                float coverage = smoothstep(.24, .76, heat.g + (broad - .5) * .19);
                clip(coverage - .012);
                float cloud = smoothstep(.35, .61, broad * .65 + noise(world * 7.2 + 5) * .35);
                float chips = ashChips(world * 7.8);
                float dust = ashChips(world * 17.3 + float2(4.3, 2.7));
                float3 tone = lerp(float3(.66,.665,.675), float3(.425,.438,.46), cloud);
                tone = lerp(tone, float3(.30,.315,.335), chips * .76);
                tone = lerp(tone, float3(.77,.778,.79), dust * .69);
                float strength = saturate(heat.r / max(heat.g, .05));
                return fixed4(tone, coverage * lerp(.68, .87, strength));
            }
            v2f vert(appdata v)
            {
                v2f o;
                if(v.uv.z>=0 && v.uv.z<1.5)
                {
                    float h=v.uv.y, t=_FireTime;
                    float bend=(sin(t*4.1+h*6+_Phase)*.09+sin(t*7.3+h*11+_Phase*1.7)*.035)*h*h;
                    if(v.uv.z>.5) v.vertex.xz+=float2(bend,-bend*.55);
                    else if(v.uv.w<.5) v.vertex.x+=bend;
                    else v.vertex.z+=bend;
                }
                if(v.uv.z>=0&&v.uv.z<.5)
                {
                    // Vertical billboard: follow camera yaw, never its pitch. No crossed-card seam.
                    float3 right=normalize(float3(UNITY_MATRIX_V[0].x,0,UNITY_MATRIX_V[0].z));
                    float width=length(unity_ObjectToWorld._m00_m10_m20);
                    float height=length(unity_ObjectToWorld._m01_m11_m21);
                    float3 root=mul(unity_ObjectToWorld,float4(0,0,0,1)).xyz;
                    float3 world=root+right*v.vertex.x*width+float3(0,v.vertex.y*height,0)
                        + _FireBend.xyz * (v.uv.y*v.uv.y);
                    o.position=mul(UNITY_MATRIX_VP,float4(world,1));o.normal=cross(right,float3(0,1,0));
                }
                else{o.position=UnityObjectToClipPos(v.vertex);o.normal=UnityObjectToWorldNormal(v.normal);}
                o.uv=v.uv;o.worldXZ=mul(unity_ObjectToWorld,v.vertex).xz;return o;
            }
            float tongue(float x,float h,float center,float height,float width,float phase,float turbulence)
            {
                // Lift the side roots a little instead of ending every card on the same
                // horizontal line. The wide base remains readable from the game camera.
                float rootLift=pow(saturate(abs(x-center)/(width+.001)),1.7)*(.035+.018*sin(phase*2.7));
                float localH=h-rootLift;
                float y=saturate(localH/height);
                float sway=sin(localH*8-_FireTime*4.7+phase)*.11*localH;
                float taper=width*pow(saturate(1-y),.78);
                float edge=abs(x-center-sway)+(turbulence-.5)*(.05+.18*h)-taper;
                float root=smoothstep(rootLift-.01,rootLift+.035,h);
                return root*(1-smoothstep(-.025,.075,edge))*(1-smoothstep(height-.045,height+.025,localH));
            }
            float tipWisp(float x,float h,float center,float baseHeight,float height,float width,float phase,float turbulence)
            {
                float localH=h-baseHeight;
                float y=saturate(localH/height);
                float sway=sin(localH*9-_FireTime*5.4+phase)*.09*(.35+localH);
                float taper=width*pow(saturate(1-y),.72);
                float edge=abs(x-center-sway)+(turbulence-.5)*(.03+.11*localH)-taper;
                return smoothstep(baseHeight-.008,baseHeight+.028,h)
                    *(1-smoothstep(-.018,.052,edge))
                    *(1-smoothstep(baseHeight+height-.035,baseHeight+height+.018,h));
            }

            float3 paintedFlame(float x, float height, float phase, float turbulence)
            {
                float bend = sin(phase * 1.31) * .10;
                float orange = max(tongue(x,height,-.11+bend,.91,.51,phase+.45,turbulence),
                    tongue(x,height,.36+bend,.63,.26,phase+2.5,turbulence) * .82);
                float gold = tongue(x,height,.025+bend,.62+.13*_CoreBoost,.37,phase-1.8,turbulence*.65);
                float cream = tongue(x,height,-.045+bend,.32+.08*_CoreBoost,.20,phase+1.2,turbulence*.53);
                if (_FirePalette > .5)
                {
                    float3 rims = flameRim();
                    float3 mid = float3(1,.27,.16);
                    float3 core = float3(1,.51,.34);
                    float3 bright = float3(1,.75,.58);
                    if (_FirePalette > 2.5)
                    {
                        mid = float3(.12,.53,1); core = float3(.34,.77,1); bright = float3(.82,.95,1);
                    }
                    else if (_FirePalette > 1.5)
                    {
                        mid = float3(1,.54,.07); core = float3(1,.73,.22); bright = float3(1,.95,.61);
                    }
                    float3 block = lerp(rims * .79, rims, step(.24,orange));
                    block = lerp(block, mid, smoothstep(.51,.68,orange));
                    block = lerp(block, core, smoothstep(.43,.63,gold));
                    return lerp(block, bright, smoothstep(.56,.76,cream));
                }
                float3 fill = lerp(FlameOuter * float3(.88,.75,.77), FlameOuter, step(.24, orange));
                fill = lerp(fill, float3(1,.47,.105), smoothstep(.51,.68,orange));
                fill = lerp(fill, float3(1,.72,.21), smoothstep(.43,.63,gold));
                return lerp(fill, float3(1,.935,.58), smoothstep(.56,.76,cream));
            }

            fixed4 frag(v2f i):SV_Target
            {
                if(i.uv.z < -1.5) return groundAsh(i.worldXZ);
                if(i.uv.z<-.5)
                {
                    // Interior cells share one quiet tint; only exposed edges are emphasized.
                    float4 d=float4(i.uv.x,1-i.uv.x,i.uv.y,1-i.uv.y);
                    // Merged danger surfaces encode each cell's exposed edges in uv.w.
                    // Standalone study meshes keep the material edge vector.
                    float encoded=floor(i.uv.w+.5);
                    float maskValue=encoded-16;
                    float4 cellEdges=float4(fmod(maskValue,2),fmod(floor(maskValue*.5),2),
                        fmod(floor(maskValue*.25),2),fmod(floor(maskValue*.125),2));
                    float4 edgeMask=encoded>=16?cellEdges:_EdgeMask;
                    float4 rim=(1-smoothstep(.015,.12,d))*edgeMask;
                    float border=max(max(rim.x,rim.y),max(rim.z,rim.w));
                    // Continuous char variation, not one radial stamp per simulation cell.
                    float hot=noise(i.worldXZ*2.3)*.7+noise(i.worldXZ*5.1)*.3;
                    // A smouldering, grey-brown bed prevents a large fire becoming a
                    // flat pink overlay on the white floor; exposed edges retain hot red.
                    float3 ground=lerp(float3(.16,.12,.09),float3(.40,.18,.055),hot);
                    ground=lerp(ground,float3(.78,.065,.008),border*.68);
                    float groundAlpha=.27+hot*.07+border*.16;
                    return flamePixel(ground, groundAlpha, hot * .40);
                }
                float2 uv=i.uv.xy;float h=uv.y;
                float phase=_Phase+i.uv.w*1.13, t=_FireTime;
                // The pattern advects upward; geometry and gameplay can remain frozen.
                float n=noise(float2(uv.x*4+phase,h*4.8-t*2.3));
                float fine=noise(float2(uv.x*9-phase,h*10-t*4));
                float turbulence=n*.72+fine*.28;
                float x=uv.x*2-1;
                float body=max(tongue(x,h,0,1.04,.61,phase,turbulence),
                    max(tongue(x,h,-.48,.74+.065*sin(t*3+phase),.32,phase+2,turbulence),
                        tongue(x,h,.47,.83+.055*sin(t*3.7+phase),.29,phase+4,turbulence)));
                if (_PaintedDetail > .5)
                {
                    // Give nearby fire patches different shoulders and tip lengths.
                    float bias = sin(phase * 1.17);
                    body = max(tongue(x,h,.09*bias,1.04,.63,phase,turbulence),
                        max(tongue(x,h,-.43,.71+.12*bias,.36,phase+2.4,turbulence),
                            tongue(x,h,.46,.78-.10*bias,.32,phase+4.3,turbulence)));
                }
                float volume=1-smoothstep(.82,1,h+(turbulence-.5)*.38);
                body=lerp(body,volume,step(.5,i.uv.z)*(1-step(1.5,i.uv.z)));
                // Narrow upper wisps rise and fade individually. They are never cut from
                // one full-width horizontal band, so a tongue cannot split like a sprite.
                float wispGate=smoothstep(.18,.72,.5+.5*sin(t*1.25+phase*3.17));
                float splitHeight=.62+.10*noise(float2(phase*1.91,t*.65));
                float wispProgress=frac(t*.27+phase*.371);
                float wispFade=smoothstep(.03,.17,wispProgress)*(1-smoothstep(.70,.98,wispProgress));
                float wispBase=splitHeight+.025+wispProgress*.15;
                float wisps=max(tipWisp(x,h,-.13,wispBase,.27,.16,phase+1.4,turbulence),
                    tipWisp(x,h,.28,wispBase+.055,.22,.11,phase+4.7,turbulence))*wispGate*wispFade;
                body=max(body,wisps);
                // Advected, asymmetric erosion only nicks the upper contour in small
                // patches. It has no common height or straight gap across a whole card.
                float erosionNoise=noise(float2((x+phase*.37)*5.7,h*8.4-t*3.6));
                float erosion=smoothstep(.70,.91,erosionNoise+(h-.60)*.16)*smoothstep(.49,.82,h);
                float alpha=body*(1-erosion*.46)*smoothstep(0,.035,h);
                float heat=saturate(1-h*1.55-abs(x)*.42+(turbulence-.5)*.28);
                heat=smoothstep(.24,.90,heat);
                heat=saturate(heat+_CoreBoost*.31*(1-smoothstep(.0,.66,h)));
                float3 color=lerp(_OuterColor.rgb,_CoreColor.rgb,heat);
                // Keep a small hot core inside a dark-orange rim rather than turning the
                // complete icon yellow. Detached tips remain orange instead of blooming.
                float rim=smoothstep(.38,.88,abs(x)*.72+h*.16+(turbulence-.5)*.14);
                color=lerp(color,_OuterColor.rgb*.72,rim*.7);
                color=lerp(color,float3(1,.29,.014),smoothstep(.62,1,h)*.28);
                color=lerp(color,float3(1,.93,.33),heat*heat*_CoreBoost*.32);
                float facing=abs(dot(normalize(i.normal),normalize(UNITY_MATRIX_V[2].xyz)));
                alpha*=lerp(.96,.48,h)*smoothstep(.025,.28,facing);
                clip(alpha-.012);
                if (_ColorBlocks > .5 && _PaintedDetail > .5)
                {
                    clip(alpha - .28);
                    float3 paint = paintedFlame(x,h,phase,turbulence);
                    if (i.uv.z > 1.5)
                    {
                        // A warm face and an ember-red shaded side follow the curved mesh.
                        float light = dot(normalize(i.normal), float3(.62,.33,-.71));
                        float side = (1 - smoothstep(.17,.58,light)) * (1 - .50*h);
                        paint *= lerp(float3(1,1,1), float3(.83,.60,.65), side * .78);
                    }
                    return fixed4(paint, 1);
                }
                return flamePixel(color, alpha, heat);
            }
        ENDCG

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ColorBlocks]
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
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
            #include "WorldOutlineMask.cginc"

            float4 maskFragment(v2f input) : SV_Target
            {
                return WriteEffectOutlineMask(input.position, frag(input).a, flameRim());
            }
            ENDCG
        }

        Pass
        {
            Name "WALL_OCCLUDED_FIRE"
            Tags { "LightMode"="FireRescueOcclusion" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One One
            BlendOp Max
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment occludedFire
            #include "WorldOutlineMask.cginc"

            sampler2D _FireRescueWallMask;
            float _FireRescueDrawHiddenFire;

            fixed4 occludedFire(v2f input) : SV_Target
            {
                clip(_FireRescueDrawHiddenFire - .5);
                clip(input.uv.z + .5);
                float2 uv = input.position.xy * _FireRescueOutlinePixelSize;
                clip(tex2D(_FireRescueWallMask, uv).r - .5);
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
                clip(.5 - OutlineDepthVisible(input.position.z, depth, .00008));
                clip(frag(input).a - .28);
                return fixed4(1, step(2.5, _FirePalette), 0, 1);
            }
            ENDCG
        }
    }
    Fallback "Transparent/VertexLit"
}
