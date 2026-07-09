using Godot;

// Applies the resident region's LightingProfile (from its EnvironmentKit) to the
// scene's sun, WorldEnvironment, and the player HeadLight. Runs once in _Ready,
// before the first frame renders, so there is no flash of the editor defaults.
public partial class LightingController : Node
{
    [Export] public NodePath SunPath;
    [Export] public NodePath WorldEnvironmentPath;
    [Export] public NodePath HeadLightPath;

    public override void _Ready()
    {
        var id = MazeData.Instance?.RegionEnvironment ?? EnvironmentId.DarkCanyon;
        var profile = EnvironmentKitRegistry.Get(id).Lighting;

        ApplySun(profile);
        ApplyEnvironment(profile);
        ApplyHeadLight(profile);

        GD.Print($"[LightingController] applied profile for {id} " +
            $"(sun={(profile.SunVisible ? profile.SunEnergy.ToString("F1") : "off")}, " +
            $"ambient={profile.AmbientEnergy:F2}, headlight={profile.HeadLightEnergy:F1})");
    }

    private void ApplySun(LightingProfile p)
    {
        var sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
        if (sun == null) return;
        sun.Visible = p.SunVisible;
        sun.LightEnergy = p.SunEnergy;
        sun.LightColor = p.SunColor;
        sun.RotationDegrees = new Vector3(p.SunPitchDeg, p.SunYawDeg, 0.0f);
    }

    private void ApplyEnvironment(LightingProfile p)
    {
        var we = GetNodeOrNull<WorldEnvironment>(WorldEnvironmentPath);
        var env = we?.Environment;
        if (env == null) return;

        if (p.AmbientEnergy > 0.0f)
        {
            env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
            env.AmbientLightColor = p.AmbientColor;
            env.AmbientLightEnergy = p.AmbientEnergy;
        }
        else
        {
            env.AmbientLightSource = Godot.Environment.AmbientSource.Disabled;
        }

        if (env.Sky?.SkyMaterial is ProceduralSkyMaterial sky)
        {
            sky.SkyTopColor = p.SkyTopColor;
            sky.SkyHorizonColor = p.SkyHorizonColor;
            sky.SkyEnergyMultiplier = p.SkyEnergy;
        }

        env.FogEnabled = p.FogEnabled;
        if (p.FogEnabled)
        {
            env.FogLightColor = p.FogColor;
            env.FogDensity = p.FogDensity;
        }
    }

    private void ApplyHeadLight(LightingProfile p)
    {
        var light = GetNodeOrNull<OmniLight3D>(HeadLightPath);
        if (light == null) return;
        light.LightEnergy = p.HeadLightEnergy;
        light.LightColor = p.HeadLightColor;
        light.OmniRange = p.HeadLightRange;
        light.OmniAttenuation = p.HeadLightAttenuation;
        light.ShadowEnabled = p.HeadLightShadow;
        var pos = light.Position;
        pos.Y = p.HeadLightHeight;
        light.Position = pos;
    }
}
