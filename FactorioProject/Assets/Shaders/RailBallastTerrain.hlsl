#ifndef PROJECTF_RAIL_BALLAST_TERRAIN_INCLUDED
#define PROJECTF_RAIL_BALLAST_TERRAIN_INCLUDED
TEXTURE2D(_RailBallastMask);
SAMPLER(sampler_RailBallastMask);
TEXTURE2D(_RailBallastAlbedo);
SAMPLER(sampler_RailBallastAlbedo);

half3 ApplyRailBallastTerrain(half3 albedo, float3 positionWS, half3 normalWS)
{
    UNITY_BRANCH
    if (_RailBallastEnabled < .5 || normalWS.y < .5) return albedo;
    float2 localUV = (positionWS.xz - _RailBallastRect.xy) * _RailBallastRect.zw;
    float2 maskUV = localUV * _RailBallastSampling.x + _RailBallastSampling.y;
    half coverage = SAMPLE_TEXTURE2D(_RailBallastMask, sampler_RailBallastMask, maskUV).r;
    coverage *= step(0, localUV.x) * step(localUV.x, 1) * step(0, localUV.y) * step(localUV.y, 1);
    UNITY_BRANCH
    if (coverage < .001h) return albedo;
    half3 gravel = SAMPLE_TEXTURE2D(_RailBallastAlbedo, sampler_RailBallastAlbedo, positionWS.xz * _RailBallastSampling.z).rgb;
    // Keep the broad mask feather; vary only the shoulder by individual stones.
    // A little ground color also shows through the core to avoid a pasted strip.
    half shoulder = 4.0h * coverage * (1.0h - coverage);
    coverage = saturate(coverage + (gravel.r - .45h) * shoulder * .22h);
    coverage = smoothstep(0.0h, 1.0h, coverage) * .9h;
    return lerp(albedo, gravel * half3(.85h, .83h, .8h), coverage);
}
#endif
