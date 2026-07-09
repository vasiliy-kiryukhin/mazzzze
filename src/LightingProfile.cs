using Godot;

// Per-region lighting mood, resolved from the region's EnvironmentKit and
// applied to the scene's sun, WorldEnvironment, and the player HeadLight by
// LightingController. One region can be a pitch-black dungeon while another
// bakes under a scorching sun — all data, no scene edits.
public sealed class LightingProfile
{
    // Sun — DirectionalLight3D. SunVisible = false makes the region dark.
    public bool SunVisible = true;
    public float SunEnergy = 1.0f;
    public Color SunColor = new Color(1.0f, 1.0f, 1.0f);
    public float SunPitchDeg = -55.0f;   // elevation; more negative = higher sun
    public float SunYawDeg = 0.0f;

    // Ambient fill — Environment. Energy <= 0 disables it entirely, so
    // downward-facing rock undersides go dark (kills the "glow from below").
    public Color AmbientColor = new Color(0.4f, 0.45f, 0.6f);
    public float AmbientEnergy = 0.0f;

    // Visible sky background — ProceduralSkyMaterial (there is no ceiling geometry).
    public Color SkyTopColor = new Color(0.02f, 0.02f, 0.03f);
    public Color SkyHorizonColor = new Color(0.03f, 0.03f, 0.04f);
    public float SkyEnergy = 0.2f;

    // Depth fog — fades distance to black so the light doesn't end in a hard ring.
    public bool FogEnabled = false;
    public Color FogColor = new Color(0.0f, 0.0f, 0.0f);
    public float FogDensity = 0.0f;

    // Player HeadLight — OmniLight3D. Range/attenuation set how far the torch reaches;
    // Height (local Y above the player) trades wall brightness for floor brightness —
    // higher = close walls hit at a grazing angle (dimmer) while the floor pool stays lit.
    public float HeadLightEnergy = 2.0f;
    public Color HeadLightColor = new Color(1.0f, 0.93f, 0.80f);
    public float HeadLightRange = 18.0f;
    public float HeadLightAttenuation = 1.6f;
    public float HeadLightHeight = 4.0f;
    // Cast shadows from the torch (omni cubemap). Makes rocks/walls block the light —
    // essential for the dungeon look, but costs a per-frame shadow render, so off by default.
    public bool HeadLightShadow = false;
}
