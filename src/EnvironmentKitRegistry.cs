using System.Collections.Generic;

public static class EnvironmentKitRegistry
{
    private static readonly Dictionary<EnvironmentId, EnvironmentKit> _kits = new();

    public static EnvironmentKit Get(EnvironmentId id)
    {
        if (!_kits.TryGetValue(id, out var kit))
        {
            kit = id switch
            {
                EnvironmentId.SlotCanyon => new SlotCanyonKit(),
                EnvironmentId.Ravine => new RavineKit(),
                EnvironmentId.DarkCanyon => new DarkCanyonKit(),
                _ => new DarkCanyonKit(),
            };
            _kits[id] = kit;
        }
        return kit;
    }
}
