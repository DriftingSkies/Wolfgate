using System.Collections.Generic;
using Content.Shared.Actions;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Shared._WG.Clown;

/// <summary>Raised by the juggle action to start or stop juggling.</summary>
public sealed partial class JuggleActionEvent : InstantActionEvent;

/// <summary>Goes on the clown player and grants the juggle action button.</summary>
[RegisterComponent]
public sealed partial class JugglingComponent : Component
{
    /// <summary>Action prototype the clown is granted.</summary>
    [DataField]
    public EntProtoId JuggleActionId = "ActionJuggle";

    /// <summary>The granted action entity.</summary>
    [DataField]
    public EntityUid? JuggleAction;
}

/// <summary>Added to the clown only while juggling, and replicated so clients can draw the items in motion.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class JugglingActiveComponent : Component
{
    /// <summary>When juggling started, which the overlay times the arcs from.</summary>
    [AutoNetworkedField]
    public TimeSpan StartTime;

    /// <summary>Items in the air, in the order the overlay spaces them.</summary>
    [AutoNetworkedField]
    public List<NetEntity> JuggledItems = new();
}

/// <summary>Consumes the Walk key while the session's player is juggling, so pressing it does nothing.</summary>
public sealed class JuggleWalkBlocker : InputCmdHandler
{
    public override bool HandleCmdMessage(
        IEntityManager entManager,
        ICommonSession? session,
        IFullInputCmdMessage message)
    {
        if (session?.AttachedEntity is not { } player)
            return false;

        return entManager.HasComponent<JugglingActiveComponent>(player);
    }
}
