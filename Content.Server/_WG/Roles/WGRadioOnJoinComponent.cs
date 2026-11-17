using Content.Shared.Radio;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Roles;

/// <summary>Reports the player's arrival on a radio channel when they spawn, for jobs with no station announcement.</summary>
[RegisterComponent]
public sealed partial class WGRadioOnJoinComponent : Component
{
    [DataField(required: true)]
    public ProtoId<RadioChannelPrototype> RadioChannel;
}
