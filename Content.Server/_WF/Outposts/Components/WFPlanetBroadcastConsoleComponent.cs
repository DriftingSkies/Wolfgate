namespace Content.Server._WF.Outposts.Components;

/// <summary>A console on an outpost that sends a short announcement to everyone on its world, once in a while.</summary>
[RegisterComponent]
public sealed partial class WFPlanetBroadcastConsoleComponent : Component
{
    /// <summary>Time between two broadcasts from this console.</summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    [DataField]
    public int MaxLength = 200;

    [ViewVariables]
    public TimeSpan NextBroadcast;

    /// <summary>Time between two distress calls from this console; a call ignores the broadcast cooldowns.</summary>
    [DataField]
    public TimeSpan DistressCooldown = TimeSpan.FromMinutes(10);

    [ViewVariables]
    public TimeSpan NextDistress;
}
