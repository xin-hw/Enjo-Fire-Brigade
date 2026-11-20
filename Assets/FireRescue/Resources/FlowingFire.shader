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
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            float4 _OuterColor, _CoreColor, _EdgeMask;
            sampler2D _HeatMap;
            float4 _HeatMapWorld;
            float _FireTime, _Phase, _CoreBoost;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; float4 uv:TEXCOORD0; float3 normal:TEXCOORD1; float2 worldXZ:TEXCOORD2; };
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 cell=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(cell),hash(cell+float2(1,0)),f.x),
                    lerp(hash(cell+float2(0,1)),hash(cell+1),f.x),f.y);
            }
            fixed4 groundFire(float2 world)
            {
                float2 source=(world-_HeatMapWorld.xy)*_HeatMapWorld.zw;
                clip(min(source.x,source.y));
                clip(1-max(source.x,source.y));
                float broad=noise(world*4.6);
                float2 bend=float2(broad-.5,noise(world*4.1+float2(13,6))-.5)*.16;
                float2 map=(world+bend-_HeatMapWorld.xy)*_HeatMapWorld.zw;
                float2 heat=tex2D(_HeatMap,map).rg;
                // The mask carries the full fire field. Interpolate its boundary
                // across neighboring cells so it has no repeated tile corners.
                float edge=smoothstep(.24,.84,heat.g+(broad-.5)*.16);
                float strength=saturate(heat.r/max(heat.g,.05));
                float grain=noise(world*17.5+float2(_FireTime*.17,-_FireTime*.12));
                float flecks=smoothstep(.68,.88,grain)*(.68+.32*sin(_FireTime*4+world.x*10+world.y*7));
                float alpha=edge*(.16+.17*strength)*lerp(.59,1,broad)+edge*flecks*.31;
                float3 color=lerp(float3(.76,.19,.028),float3(1,.65,.16),saturate(broad*.48+flecks*.7));
                clip(alpha-.012);
                return fixed4(color,alpha);
            }
            v2f vert(appdata v)
            {
                v2f o;
                if(v.uv.z>=0)
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
                    float3 world=root+right*v.vertex.x*width+float3(0,v.vertex.y*height,0);
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
            fixed4 frag(v2f i):SV_Target
            {
                if(i.uv.z < -1.5) return groundFire(i.worldXZ);
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
                    return fixed4(ground,.27+hot*.07+border*.16);
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
                float volume=1-smoothstep(.82,1,h+(turbulence-.5)*.38);
                body=lerp(body,volume,step(.5,i.uv.z));
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
                return fixed4(color,alpha);
            }
            ENDCG
        }
    }
    Fallback "Transparent/VertexLit"
}
