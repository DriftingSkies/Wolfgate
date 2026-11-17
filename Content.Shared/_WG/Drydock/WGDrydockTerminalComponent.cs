namespace Content.Shared._WG.Drydock;

/// <summary>
/// A drydock terminal: works every berth console multitool-linked to it on its grid, from one seat. It owns no job;
/// each berth runs its own, so the berths can work several ships at once. It locks berths against walk-in work and
/// bookings, and on a commanded ship sets the ship's drydock price.
/// </summary>
[RegisterComponent]
public sealed partial class WGDrydockTerminalComponent : Component
{
    /// <summary>Source port the terminal links its berth consoles on.</summary>
    public const string BerthPort = "WGDrydockBerthSender";

    /// <summary>Sink port a berth console takes a terminal's link on.</summary>
    public const string BerthReceiverPort = "WGDrydockBerthReceiver";

    /// <summary>Last berth states pushed to viewers, as a key. Runtime only.</summary>
    public string? LastPushed;
}
