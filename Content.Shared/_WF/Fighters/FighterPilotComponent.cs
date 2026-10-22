using Content.Shared.Movement.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Fighters;

/// <summary>On whoever is flying a fighter.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FighterPilotComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Fighter;

    /// <summary>Shuttle keys currently held.</summary>
    [ViewVariables]
    public ShuttleButtons HeldButtons;
}
