Shader "FireRescue/ColorBlockPowder"
{
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        CGINCLUDE
        #include "UnityCG.cginc"
        #include "WorldOutlineMask.cginc"

        static const float3 PowderShade = float3(.70,.565,.375);
        static const float3 PowderBody = float3(.934,.843,.622);
        static const float3 PowderLight = float3(1,.960,.839);

        struct Vertex
        {
            float4 position : POSITION;
            float4 uv : TEXCOORD0;
        };
        struct Fragment
        {
            float4 position : SV_POSITION;
            float3 uv : TEXCOORD0;
        };

        Fragment vert(Vertex vertex)
        {
            Fragment o;
            o.position = UnityObjectToClipPos(vertex.position);
            o.uv = vertex.uv.xyz;
            return o;
        }

        float lobe(float2 p, float2 center, float2 radius)
        {
            float2 q = (p - center) / radius;
            return dot(q, q);
        }

        fixed4 powder(Fragment input) : SV_Target
        {
            float2 p = input.uv.xy * 2 - 1;
            float variation = input.uv.z;
            float bend = (variation - .5) * .30;
            float outline = lobe(p, float2(-.20,bend), float2(.66,.68));
            outline = min(outline, lobe(p, float2(.32,-.12), float2(.49,.54)));
            outline = min(outline, lobe(p, float2(.22,.30 + bend), float2(.45,.52)));
            outline += sin(p.x * 8 + variation * 9) * sin(p.y * 7) * .075;
            clip(1 - outline);

            float light = p.y - p.x * .26 + sin(p.x * 6 + variation * 5) * .14;
            float3 shade = lerp(PowderShade, PowderBody, step(-.33, light));
            shade = lerp(shade, PowderLight, step(.23, light));
            return fixed4(shade, 1);
        }
        ENDCG

        Pass
        {
            ZWrite On
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment powder
            ENDCG
        }
        Pass
        {
            Name "OUTLINE_MASK"
            Tags { "LightMode"="FireRescueOutline" }
            ZWrite On
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment mask

            float4 mask(Fragment input) : SV_Target
            {
                return WriteEffectOutlineMask(input.position, powder(input).a, PowderShade);
            }
            ENDCG
        }
    }
}
