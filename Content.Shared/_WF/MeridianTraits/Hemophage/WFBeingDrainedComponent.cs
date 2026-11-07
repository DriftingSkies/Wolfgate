using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WF.MeridianTraits.Hemophage;

/// <summary>Held fast by a hemophage's bite: no moving, breaking free, using things or fighting until it ends.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class WFBeingDrainedComponent : Component
{
    /// <summary>The hemophage biting.</summary>
    [DataField, AutoNetworkedField]
    public EntityUid Drinker;

    /// <summary>When the hold lifts by itself, if the bite never ends cleanly.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan Until;
}
