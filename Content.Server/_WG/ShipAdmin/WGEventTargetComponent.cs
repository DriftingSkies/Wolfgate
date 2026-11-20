using Robust.Shared.Player;

namespace Content.Server._WG.ShipAdmin;

/// <summary>
/// On a game rule staff started from Shipmaster: the station and grid it is aimed at. Rules that pick a random
/// station or tile pick these instead, or nothing when the rule can't use them.
/// </summary>
[RegisterComponent]
public sealed partial class WGEventTargetComponent : Component
{
    [DataField]
    public EntityUid Station;

    [DataField]
    public EntityUid Grid;

    /// <summary>The admin who aimed it, told if it misses.</summary>
    [ViewVariables]
    public ICommonSession? Admin;

    /// <summary>Set once the rule's pickers found nothing on the target, so the admin is told once.</summary>
    [ViewVariables]
    public bool Missed;
}

/// <summary>Raised on an aimed rule when its start found nothing it could use on the target.</summary>
[ByRefEvent]
public readonly record struct WGEventTargetMissedEvent;
