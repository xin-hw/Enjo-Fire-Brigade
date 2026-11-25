Shader "FireRescue/PowderMist"
{
    SubShader
    {
        Tags { "Queue"="Transparent+22" "RenderType"="Transparent" "IgnoreProjector"="True" }
        CGINCLUDE
        #include "UnityCG.cginc"
        static const float3 PowderShade = float3(.66,.49,.32);
        static const float3 PowderBody = float3(.83,.69,.49);
        static const float3 PowderLight = float3(.98,.89,.73);

        struct Vertex
        {
            float4 position : POSITION;
            float4 uv : TEXCOORD0;
        };
        struct Fragment
        {
            float4 position : SV_POSITION;
            float4 uv : TEXCOORD0;
        };

        Fragment vert(Vertex vertex)
        {
            Fragment o;
            o.position = UnityObjectToClipPos(vertex.position);
            o.uv = vertex.uv;
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
            float bend = (variation - .5) * .34;
            float shape = lobe(p, float2(-.22,bend), float2(.74,.70));
            shape = min(shape, lobe(p, float2(.33,-.13), float2(.47,.57)));
            shape = min(shape, lobe(p, float2(.13,.26 + bend), float2(.56,.63)));
            float grainPattern = sin(p.x * 10 + p.y * 3 + variation * 11) * sin(p.y * 9 - variation * 7);
            float density = 1 - smoothstep(.16,1.12,shape + grainPattern * .12);
            density *= smoothstep(0,.17,1-max(abs(p.x),abs(p.y)));
            float light = p.y - p.x * .19 + grainPattern * .20;
            float3 shade = lerp(PowderShade, PowderBody, smoothstep(-.48,.14,light));
            shade = lerp(shade, PowderLight, smoothstep(.05,.58,light) * .79);
            float alpha = density * input.uv.w;
            clip(alpha - .003);
            return fixed4(shade, alpha);
        }
        ENDCG

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment powder
            ENDCG
        }
    }
}
