using Godot;
using System.Collections.Generic;

public sealed class RavineKit : EnvironmentKit
{
    public RavineKit()
    {
        LoadCliffPrototypes();
        RockMaterial = GD.Load<Material>(
            "res://art/RockPack1/Materials/Cliff_Material_Photoscan.tres");
        ComputeBaseScales();

        // Overcast grey ravine: soft diffuse sun, cool grey ambient and sky,
        // faint haze. A mid player torch reads against the flat daylight.
        Lighting = new LightingProfile
        {
            SunVisible = true,
            SunEnergy = 1.2f,
            SunColor = new Color(0.85f, 0.87f, 0.9f),
            SunPitchDeg = -55.0f,
            AmbientColor = new Color(0.45f, 0.47f, 0.5f),
            AmbientEnergy = 0.5f,
            SkyTopColor = new Color(0.4f, 0.42f, 0.46f),
            SkyHorizonColor = new Color(0.55f, 0.56f, 0.58f),
            SkyEnergy = 0.7f,
            FogEnabled = true,
            FogColor = new Color(0.4f, 0.42f, 0.45f),
            FogDensity = 0.012f,
            HeadLightEnergy = 1.0f,
            HeadLightColor = new Color(1.0f, 0.96f, 0.88f),
            HeadLightRange = 14.0f,
            HeadLightAttenuation = 1.4f,
        };
    }

    public override List<RockPlacement> PlaceRocks(
        Vector3 cellCenterLocal, ulong seed, WallAxis axis)
    {
        return SingleCenteredRock(cellCenterLocal, seed, axis);
    }
}
