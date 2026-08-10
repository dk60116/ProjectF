#ifndef PROJECTF_TERRAIN_WATER_LIGHTING_INCLUDED
#define PROJECTF_TERRAIN_WATER_LIGHTING_INCLUDED

half _WorldWaterBrightness;

half GetWorldWaterBrightness()
{
    return _WorldWaterBrightness > 0.001h
        ? saturate(_WorldWaterBrightness)
        : 1.0h;
}

#endif
