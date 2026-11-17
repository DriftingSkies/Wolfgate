using Robust.Shared.Map;

namespace Content.Server._WG.Weapons.Projectiles;

/// <summary>A round the sweep holds on what it struck, until its hit lands there.</summary>
[RegisterComponent]
public sealed partial class WGSweepHoldComponent : Component
{
    /// <summary>What the round struck: while held it can hit nothing else.</summary>
    public EntityUid Target;

    /// <summary>Where it struck.</summary>
    public EntityCoordinates At;
}
