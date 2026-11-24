#ifndef FIRE_RESCUE_WORLD_OUTLINE_MASK
#define FIRE_RESCUE_WORLD_OUTLINE_MASK

#include "UnityCG.cginc"

UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
float2 _FireRescueOutlinePixelSize;
float _FireRescueWritingOutlineMask;

float OutlineDepthVisible(float depth, float opaqueDepth, float tolerance)
{
#if defined(UNITY_REVERSED_Z)
    return step(opaqueDepth - tolerance, depth);
#else
    return step(depth, opaqueDepth + tolerance);
#endif
}

float3 DarkenOutlineColour(float3 colour)
{
    return saturate(colour) * .60 + .032;
}

float4 WriteOutlineMask(float4 position, float3 colour)
{
    clip(_FireRescueWritingOutlineMask - .5);
    float opaqueDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, position.xy * _FireRescueOutlinePixelSize);
    clip(OutlineDepthVisible(position.z, opaqueDepth, .00008) - .5);
    // Negative depth marks a covered pixel. Clear pixels store zero.
    return float4(colour, -max(position.z, .00001));
}

float4 WriteEffectOutlineMask(float4 position, float opacity, float3 colour)
{
    clip(opacity - .28);
    return WriteOutlineMask(position, DarkenOutlineColour(colour));
}

#endif
