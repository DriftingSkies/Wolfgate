using Content.Shared.Storage;

namespace Content.Server.Worldgen.Components.Debris;

public sealed partial class NoiseDrivenDebrisSelectorComponent
{
    /// <summary>The debris table as written in the prototype.</summary>
    public IReadOnlyList<EntitySpawnEntry> WfEntries => _entries;
}

public sealed partial class SimpleDebrisSelectorComponent
{
    /// <summary>The debris table as written in the prototype.</summary>
    public IReadOnlyList<EntitySpawnEntry> WfEntries => _entries;
}
