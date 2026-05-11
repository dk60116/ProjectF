float4 _ProjectFWaterSpecularLightDirection;

void DirLight_half(float3 WorldPos, out half3 Direction, out half3 Color, out half Attenuation)
{
#if SHADERGRAPH_PREVIEW
	Direction = half3(0.5, 0.5, 0);
	Color = 1;
	Attenuation = 1;
#else
#if SHADOWS_SCREEN
	half4 clipPos = TransformWorldToHClip(WorldPos);
	half4 shadowCoord = ComputeScreenPos(clipPos);
#else
	half4 shadowCoord = TransformWorldToShadowCoord(WorldPos);
#endif
	Light mainLight = GetMainLight(shadowCoord);
	Direction = mainLight.direction;
	half3 waterSpecularDirection = half3(_ProjectFWaterSpecularLightDirection.xyz);
	if (dot(waterSpecularDirection, waterSpecularDirection) > 0.0001)
	{
		Direction = normalize(waterSpecularDirection);
	}
	Color = mainLight.color;
	Attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
#endif
}
