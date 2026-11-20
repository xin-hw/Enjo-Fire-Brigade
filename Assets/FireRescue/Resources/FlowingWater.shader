Shader "FireRescue/FlowingWater"
{
    Properties
    {
        _Color ("Water tint", Color) = (0.18,0.65,0.86,1)
        _EdgeColor ("Surface highlight", Color) = (0.76,0.94,1,1)
        _Opacity ("Water density", Range(0,1)) = 0.64
        _Refraction ("Screen refraction", Range(0,0.02)) = 0.0035
        _PresentationTime ("Local flow clock", Float) = 0
        _WetFade ("Wet film remaining", Range(0,1)) = 1
        _ImpactAge ("Seconds since contact", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        GrabPass { "_FireRescueWaterBackground" }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            float4 _Color,_EdgeColor;
            float _Opacity,_PresentationTime,_Refraction,_WetFade,_ImpactAge;
            sampler2D _FireRescueWaterBackground;
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float4 tangent:TANGENT; float4 uv:TEXCOORD0; };
            struct v2f { float4 position:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; float4 uv:TEXCOORD2; float4 grab:TEXCOORD3; float4 tangent:TEXCOORD4; };
            v2f vert(appdata v)
            {
                v2f o;o.position=UnityObjectToClipPos(v.vertex);o.world=mul(unity_ObjectToWorld,v.vertex).xyz;
                o.normal=UnityObjectToWorldNormal(v.normal);o.uv=v.uv;o.grab=ComputeGrabScreenPos(o.position);
                o.tangent=float4(UnityObjectToWorldDir(v.tangent.xyz),v.tangent.w*unity_WorldTransformParams.w);return o;
            }
            fixed4 frag(v2f i):SV_Target
            {
                float puddle=step(i.uv.z,-0.5);
                float core=saturate(i.uv.z);
                float2 uv=i.uv.xy;
                float t=_PresentationTime;
                float3 view=normalize(lerp(normalize(_WorldSpaceCameraPos-i.world),normalize(UNITY_MATRIX_V[2].xyz),unity_OrthoParams.w));
                float3 light=normalize(UnityWorldSpaceLightDir(i.world));
                float3 surface=normalize(i.normal),along=normalize(i.tangent.xyz);
                float3 crossAxis=cross(surface,along);
                float3 across=crossAxis*rsqrt(max(dot(crossAxis,crossAxis),.0001))*i.tangent.w;
                if(puddle>.5)
                {
                    // Contact-space ripples also work on a vertical wall. Old world-XZ
                    // shading flattened wall splashes into uniform opaque round stamps.
                    float2 p=uv-.5;float r=length(p)*2;
                    float irregular=sin(p.x*21+p.y*17)*sin(p.y*23-p.x*11);
                    float edge=1-smoothstep(.45,.98,r+irregular*.075);
                    float wave=sin(r*19-_ImpactAge*23+irregular*1.8);
                    // Short broken contact arcs, not three perfect target rings.
                    float arcs=smoothstep(.05,.8,sin(p.x*31+p.y*17)*cos(p.y*19-p.x*9));
                    float pulse=pow(saturate(wave),5)*arcs*exp(-_ImpactAge*5)*smoothstep(.04,.24,r);
                    float3 n=normalize(surface+(along*p.x+across*p.y)*wave*.12);
                    float glint=pow(saturate(dot(n,normalize(view+light))),70)*.38;
                    float3 c=lerp(_Color.rgb,_EdgeColor.rgb,saturate(.22+pulse*.35+glint));
                    return fixed4(c,edge*_WetFade*(_Opacity*.3+pulse*.08+glint*.14));
                }
                // Periodic around the tube seam, non-periodic along its length. Advected
                // narrow highlights read as rushing water, not painted solid-blue plastic.
                float angle=uv.x*6.2831853;
                float a=sin(uv.y*19-t*37+sin(angle*3+uv.y*8-t*6)*1.7);
                float b=sin(uv.y*43-t*59+cos(angle*5+uv.y*13+t*3));
                float streak=pow(saturate((a*.6+b*.4)*.7+.25),3);
                float3 n=normalize(surface+(along*b+across*a)*.10*(.4+core));
                float rim=pow(1-saturate(dot(n,view)),2.2);
                float spec=pow(saturate(dot(n,normalize(view+light))),88)*.65;
                float foam=saturate(i.uv.w)*pow(saturate(b*.5+.5),3);
                float3 color=lerp(_Color.rgb*float3(.67,.86,.94),_EdgeColor.rgb,saturate(rim*.68+streak*.55+foam*.7));
                color*=.78+.22*saturate(dot(n,light));
                color+=_EdgeColor.rgb*(spec+streak*.16);
                float2 distortion=(mul((float3x3)UNITY_MATRIX_V,n).xy*.5+float2(a,b)*.15)*_Refraction*(1-puddle*.75);
                float4 grab=i.grab;grab.xy+=distortion*grab.w;
                float3 transmitted=tex2Dproj(_FireRescueWaterBackground,UNITY_PROJ_COORD(grab)).rgb;
                color=lerp(transmitted,color,saturate(.68+rim*.25+foam*.15));
                float alpha=saturate(_Opacity*(.64+rim*.48)+streak*.20+foam*.12);
                // Thin end caps and feathered puddles don't read as sawn plastic ends/decals.
                alpha*=i.uv.w<-.5?.22:1;
                alpha*=lerp(1,pow(saturate(1-length(i.uv.xy-.5)*2),.42),puddle);
                return fixed4(color,alpha);
            }
            ENDCG
        }
    }
    Fallback "Transparent/VertexLit"
}
