using Godot;
using System.Collections.Generic;

public sealed class DarkCanyonKit : EnvironmentKit
{
    public DarkCanyonKit()
    {
        LoadCliffPrototypes();
        RockMaterial = GD.Load<Material>(
            "res://art/RockPack1/Materials/Cliff_Material_Dark.tres");
        ComputeBaseScales();

        // Pitch-black dungeon: no sun, no ambient fill, near-black sky, and a
        // subtle black fog so distance fades out. Only the warm player torch lights the way.
        Lighting = new LightingProfile
        {
            SunVisible = false,
            AmbientEnergy = 0.0f,
            SkyTopColor = new Color(0.015f, 0.015f, 0.02f),
            SkyHorizonColor = new Color(0.02f, 0.02f, 0.025f),
            SkyEnergy = 0.12f,
            FogEnabled = true,
            FogColor = new Color(0.01f, 0.01f, 0.015f),
            FogDensity = 0.035f,
            HeadLightEnergy = 2.5f,
            HeadLightColor = new Color(1.0f, 0.945f, 0.83f),
            HeadLightRange = 22.5f,
            HeadLightAttenuation = 1.25f,
            HeadLightHeight = 4.0f,
            HeadLightShadow = true,
        };
    }

    public override List<RockPlacement> PlaceRocks(
        Vector3 cellCenterLocal, ulong seed, WallAxis axis)
    {
        return SingleCenteredRock(cellCenterLocal, seed, axis);
    }
}
