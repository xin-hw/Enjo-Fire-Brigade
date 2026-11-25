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
        #pragma target 3.0
        #pragma surface surf Lambert fullforwardshadows addshadow
        fixed4 _Color;
        float _ModuleSize,_SeamWidth,_JointContrast;
        float4 _CutawayPlayer, _CutawayView;
        struct Input { float3 worldPos; float3 worldNormal; float4 screenPos; };
        float joint(float coordinate)
        {
            float u=frac(coordinate/max(.1,_ModuleSize));
            float distance=min(u,1-u)*max(.1,_ModuleSize);
            return 1-smoothstep(_SeamWidth,_SeamWidth+fwidth(coordinate)*.8,distance);
        }

        void revealPlayer(Input IN)
        {
            // Match the gameplay view and its depth pass. Keep the full wall in the light's shadow pass.
            float4 depthRow = UNITY_MATRIX_V[2];
            if (_CutawayPlayer.w < .5 || dot(depthRow.xyz, _CutawayView.xyz) < .999 ||
                abs(depthRow.w - _CutawayView.w) > .05) return;

            float3 center = _CutawayPlayer.xyz + float3(0, .80, 0);
            float3 toWall = IN.worldPos - center;
            float3 relative = mul((float3x3)UNITY_MATRIX_V, toWall);
            // A tall wall top can be nearer in view depth while still behind the player's feet.
            float2 groundView = depthRow.xz;
            float groundDepth = dot(toWall.xz, groundView) / max(.0001, length(groundView));
            float2 capsule = float2(relative.x, max(0, abs(relative.y) - .23));
            float opening = 1 - smoothstep(.62, .78, length(capsule));
            opening *= smoothstep(.025, .20, relative.z) * (1 - smoothstep(1.65, 2.05, relative.z));
            opening *= smoothstep(.015, .11, groundDepth);
            // Leave a visible strip at the foot of the wall to show the boundary.
            opening *= smoothstep(.08, .22, IN.worldPos.y - _CutawayPlayer.y);

            float2 pixel = floor(IN.screenPos.xy / IN.screenPos.w * _ScreenParams.xy);
            float threshold = .125 + dot(fmod(pixel, 2), float2(.50, .25));
            clip(1 - opening - threshold);
        }

        void surf(Input IN,inout SurfaceOutput o)
        {
            revealPlayer(IN);
            float3 n=abs(normalize(IN.worldNormal));
            float horizontal=n.x>n.z?IN.worldPos.z:IN.worldPos.x;
            float side=max(joint(horizontal),joint(IN.worldPos.y-.1));
            float top=max(joint(IN.worldPos.x),joint(IN.worldPos.z));
            float lines=lerp(side,top,smoothstep(.5,.9,n.y));
            // A flush painted/pressed joint only. Never a gap or a bevel at each block.
            o.Albedo=_Color.rgb*(1-lines*_JointContrast);o.Alpha=1;
        }
        ENDCG

        Pass
        {
            Name "FIRE_OCCLUDER"
            Tags { "LightMode"="FireRescueOcclusion" }
            ZWrite Off
            ZTest Always
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "../../Resources/WorldOutlineMask.cginc"

            float _FireRescueDrawHiddenFire;

            float4 vert(float4 position : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(position);
            }

            fixed4 frag(float4 position : SV_POSITION) : SV_Target
            {
                clip(_FireRescueDrawHiddenFire - .5);
                float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, position.xy * _FireRescueOutlinePixelSize);
                // Only the wall pixels that survived the ordinary depth test and player cutaway.
                clip(.00008 - abs(position.z - depth));
                return 1;
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}
