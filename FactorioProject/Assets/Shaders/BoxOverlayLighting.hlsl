#ifndef PROJECTF_BOX_OVERLAY_LIGHTING_INCLUDED
#define PROJECTF_BOX_OVERLAY_LIGHTING_INCLUDED

#define PROJECTF_BOX_DISPLAY_LIGHT_COUNT 8

float4 _BoxDisplayLightPositionAndInvRangeSqr[PROJECTF_BOX_DISPLAY_LIGHT_COUNT];
half4 _BoxDisplayLightColorAndIntensity[PROJECTF_BOX_DISPLAY_LIGHT_COUNT];
int _BoxDisplayLightCount;

half3 GetBoxOverlayFacingNormal(
    float3 positionWS,
    half3 normalWS)
{
    normalWS = normalize(normalWS);
    half3 viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
    if (dot(normalWS, viewDirectionWS) < 0.0h)
    {
        normalWS = -normalWS;
    }

    return normalWS;
}

half3 GetBoxOverlayAmbientAndLocalLighting(
    float3 positionWS,
    half3 normalWS)
{
    half3 lighting = SampleSH(normalWS);

    UNITY_LOOP
    for (int i = 0; i < _BoxDisplayLightCount; i++)
    {
        float4 localLightPositionAndInvRangeSqr =
            _BoxDisplayLightPositionAndInvRangeSqr[i];
        half4 localLightColorAndIntensity = _BoxDisplayLightColorAndIntensity[i];
        float3 toLocalLight = localLightPositionAndInvRangeSqr.xyz - positionWS;
        float distanceSqr = max(dot(toLocalLight, toLocalLight), 0.01);
        float inverseDistance = rsqrt(distanceSqr);
        float normalizedDistanceSqr = distanceSqr
            * localLightPositionAndInvRangeSqr.w;
        half smoothFactor = saturate(
            1.0h - (normalizedDistanceSqr * normalizedDistanceSqr));
        half attenuation = smoothFactor * smoothFactor
            * inverseDistance * inverseDistance;
        half3 localLightColor = localLightColorAndIntensity.rgb
            * localLightColorAndIntensity.a
            * attenuation;
        lighting += LightingLambert(
            localLightColor,
            toLocalLight * inverseDistance,
            normalWS);
    }

    return max(lighting, 0.0h);
}

half3 GetBoxOverlayVertexLighting(
    float3 positionWS,
    half3 normalWS)
{
    normalWS = GetBoxOverlayFacingNormal(positionWS, normalWS);
    half3 lighting = GetBoxOverlayAmbientAndLocalLighting(positionWS, normalWS);
    Light mainLight = GetMainLight();
    half3 mainLightColor = mainLight.color * mainLight.distanceAttenuation;
    lighting += LightingLambert(mainLightColor, mainLight.direction, normalWS);
    return max(lighting, 0.0h);
}

half3 GetBoxOverlayTwoSidedFragmentLighting(
    float3 positionWS,
    half3 normalWS)
{
    normalWS = normalize(normalWS);
    half3 lighting = max(SampleSH(normalWS), SampleSH(-normalWS));

    Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
    half mainDiffuse = lerp(
        0.25h,
        1.0h,
        saturate(abs(dot(normalWS, mainLight.direction))));
    lighting += mainLight.color
        * mainLight.distanceAttenuation
        * mainLight.shadowAttenuation
        * mainDiffuse;

    UNITY_LOOP
    for (int i = 0; i < _BoxDisplayLightCount; i++)
    {
        float4 localLightPositionAndInvRangeSqr =
            _BoxDisplayLightPositionAndInvRangeSqr[i];
        half4 localLightColorAndIntensity = _BoxDisplayLightColorAndIntensity[i];
        float3 toLocalLight = localLightPositionAndInvRangeSqr.xyz - positionWS;
        float distanceSqr = max(dot(toLocalLight, toLocalLight), 0.01);
        float inverseDistance = rsqrt(distanceSqr);
        float normalizedDistanceSqr = distanceSqr
            * localLightPositionAndInvRangeSqr.w;
        half smoothFactor = saturate(
            1.0h - (normalizedDistanceSqr * normalizedDistanceSqr));
        half attenuation = smoothFactor * smoothFactor
            * inverseDistance * inverseDistance;
        half localDiffuse = lerp(
            0.25h,
            1.0h,
            saturate(abs(dot(normalWS, toLocalLight * inverseDistance))));
        lighting += localLightColorAndIntensity.rgb
            * localLightColorAndIntensity.a
            * attenuation
            * localDiffuse;
    }

    return max(lighting, 0.0h);
}

#endif
