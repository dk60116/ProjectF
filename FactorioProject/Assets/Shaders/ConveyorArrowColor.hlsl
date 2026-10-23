#ifndef PROJECTF_CONVEYOR_ARROW_COLOR_INCLUDED
#define PROJECTF_CONVEYOR_ARROW_COLOR_INCLUDED
TEXTURE2D(_ArrowMask);
SAMPLER(sampler_ArrowMask);

// Mask UV follows the exact albedo sample, including scrolling, slope length,
// seam cropping and the corner's remapped flow UV. Replace the printed gray
// with the selected ink color; lighting still shades it and alpha is unchanged.
half4 ApplyConveyorArrowColor(half4 sourceSample, float2 uv)
{
    half mask = SAMPLE_TEXTURE2D(_ArrowMask, sampler_ArrowMask, uv).r;
    sourceSample.rgb = lerp(sourceSample.rgb, _ArrowColor.rgb, saturate(mask * _ArrowColor.a));
    return sourceSample;
}
#endif
