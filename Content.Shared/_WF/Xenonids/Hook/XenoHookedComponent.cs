using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.Xenonids.Hook;

[RegisterComponent, NetworkedComponent]
public sealed partial class XenoHookedComponent : Component
{
    /// <summary>When the hook lets go even if the pull never landed, as in zero gravity where a throw never stops.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan ReleaseAt;

    /// <summary>The hook holding this, so letting go clears it from that source's list and no other's.</summary>
    [DataField]
    public EntityUid? Source;
}
