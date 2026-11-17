using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._WG.MeridianTraits.Hemophage;

/// <summary>Taste is dulled: a hemophage's tumor doesn't react to what it eats or drinks.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class WGTasteSuppressedComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan Until;
}
