Shader "FireRescue/ConnectedPlaster"
{
    Properties
    {
        _Color ("Matte beige", Color) = (0.91,0.87,0.78,1)
        _ModuleSize ("Square face size (m)", Float) = 0.72
        _SeamWidth ("Flush joint width (m)", Float) = 0.003
        _JointContrast ("Subtle joint contrast", Range(0,0.2)) = 0.035
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 150
        CGPROGRAM
        #pragma surface surf Lambert fullforwardshadows
        fixed4 _Color;
        float _ModuleSize,_SeamWidth,_JointContrast;
        struct Input { float3 worldPos; float3 worldNormal; };
        float joint(float coordinate)
        {
            float u=frac(coordinate/max(.1,_ModuleSize));
            float distance=min(u,1-u)*max(.1,_ModuleSize);
            return 1-smoothstep(_SeamWidth,_SeamWidth+fwidth(coordinate)*.8,distance);
        }
        void surf(Input IN,inout SurfaceOutput o)
        {
            float3 n=abs(normalize(IN.worldNormal));
            float horizontal=n.x>n.z?IN.worldPos.z:IN.worldPos.x;
            float side=max(joint(horizontal),joint(IN.worldPos.y-.1));
            float top=max(joint(IN.worldPos.x),joint(IN.worldPos.z));
            float lines=lerp(side,top,smoothstep(.5,.9,n.y));
            // A flush painted/pressed joint only. Never a gap or a bevel at each block.
            o.Albedo=_Color.rgb*(1-lines*_JointContrast);o.Alpha=1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
