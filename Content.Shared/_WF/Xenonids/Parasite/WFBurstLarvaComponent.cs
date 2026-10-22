using Robust.Shared.GameStates;

namespace Content.Shared._WF.Xenonids.Parasite;

/// <summary>A larva that burst out of a host; remembers the host so taking it over can seal the host's fate.</summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(SharedXenoParasiteSystem))]
public sealed partial class WFBurstLarvaComponent : Component
{
    [DataField]
    public EntityUid? Host;
}
