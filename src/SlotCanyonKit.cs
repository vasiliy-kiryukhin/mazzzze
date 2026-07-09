using Godot;
using System.Collections.Generic;

public sealed class SlotCanyonKit : EnvironmentKit
{
    public SlotCanyonKit()
    {
        LoadCliffPrototypes();
        RockMaterial = GD.Load<Material>(
            "res://art/RockPack1/Materials/Cliff_Material_Red_Sand.tres");
        ComputeBaseScales();

        // Scorching open canyon: harsh warm sun, warm ambient bounce, bright sky.
        // The player torch is dialed down — the sun already lights everything.
        Lighting = new LightingProfile
        {
            SunVisible = true,
            SunEnergy = 3.0f,
            SunColor = new Color(1.0f, 0.9f, 0.72f),
            SunPitchDeg = -62.0f,
            AmbientColor = new Color(0.55f, 0.45f, 0.35f),
            AmbientEnergy = 0.45f,
            SkyTopColor = new Color(0.35f, 0.45f, 0.7f),
            SkyHorizonColor = new Color(0.7f, 0.6f, 0.45f),
            SkyEnergy = 1.0f,
            FogEnabled = false,
            HeadLightEnergy = 0.5f,
            HeadLightColor = new Color(1.0f, 0.96f, 0.86f),
            HeadLightRange = 12.0f,
            HeadLightAttenuation = 1.2f,
        };
    }

    public override List<RockPlacement> PlaceRocks(
        Vector3 cellCenterLocal, ulong seed, WallAxis axis)
    {
        return SingleCenteredRock(cellCenterLocal, seed, axis);
    }
}
