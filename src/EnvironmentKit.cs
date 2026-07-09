using Godot;
using System.Collections.Generic;

public abstract class EnvironmentKit
{
    public Mesh[] Prototypes { get; protected set; }
    public Material RockMaterial { get; protected set; }
    public LightingProfile Lighting { get; protected set; } = new LightingProfile();
    public float[] BaseScales { get; private set; }
    public bool[] LongAxisIsX { get; private set; }

    protected float FootprintFraction = 2.0f;

    private static readonly int[] CliffIds = { 1, 2, 3, 4, 6, 7, 8 };

    protected void LoadCliffPrototypes()
    {
        Prototypes = new Mesh[CliffIds.Length];
        for (int i = 0; i < CliffIds.Length; i++)
        {
            Prototypes[i] = GD.Load<Mesh>(
                $"res://art/RockPack1/Models/meshes/Cliff_models_cliff{CliffIds[i]}_mesh.res");
        }
    }

    protected void ComputeBaseScales()
    {
        BaseScales = new float[Prototypes.Length];
        LongAxisIsX = new bool[Prototypes.Length];
        for (int i = 0; i < Prototypes.Length; i++)
        {
            var size = Prototypes[i].GetAabb().Size;
            float longest = Mathf.Max(size.X, Mathf.Max(size.Y, size.Z));
            BaseScales[i] = longest > 0.001f
                ? MazeData.CellWorldSize * FootprintFraction / longest
                : 1.0f;
            LongAxisIsX[i] = size.X >= size.Z;
        }
    }

    public abstract List<RockPlacement> PlaceRocks(
        Vector3 cellCenterLocal, ulong seed, WallAxis axis);

    protected List<RockPlacement> SingleCenteredRock(
        Vector3 cellCenterLocal, ulong seed, WallAxis axis)
    {
        int proto = (int)(seed % (ulong)Prototypes.Length);
        float s = BaseScales[proto];
        float yaw = 0.0f;
        if (axis != WallAxis.None && LongAxisIsX[proto] != (axis == WallAxis.X))
            yaw = Mathf.Pi / 2.0f;
        return new List<RockPlacement>
        {
            new RockPlacement(proto,
                MakeTransform(cellCenterLocal, new Vector3(0.0f, yaw, 0.0f),
                    new Vector3(s, s, s))),
        };
    }

    protected static RandomNumberGenerator Rng(ulong seed)
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = seed;
        return rng;
    }

    protected static Transform3D MakeTransform(Vector3 pos, Vector3 eulerRad, Vector3 scale)
    {
        var basis = Basis.FromEuler(eulerRad).Scaled(scale);
        return new Transform3D(basis, pos);
    }
}
