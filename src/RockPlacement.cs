using Godot;

public readonly struct RockPlacement
{
    public readonly int PrototypeIndex;
    public readonly Transform3D Transform;

    public RockPlacement(int prototypeIndex, Transform3D transform)
    {
        PrototypeIndex = prototypeIndex;
        Transform = transform;
    }
}
