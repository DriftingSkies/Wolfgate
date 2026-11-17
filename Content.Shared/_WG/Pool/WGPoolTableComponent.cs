namespace Content.Shared._WG.Pool;

/// <summary>A table that 8-ball is played on. The game lives on the table's root piece.</summary>
[RegisterComponent]
public sealed partial class WGPoolTableComponent : Component
{
    /// <summary>The table is built from several of these side by side, which share one game.</summary>
    [DataField]
    public bool MultiTile;

    /// <summary>How far from any piece of the table a player may stand.</summary>
    [DataField]
    public float Range = 2f;

    [ViewVariables]
    public PoolGame? Game;

    /// <summary>The player on each side.</summary>
    [ViewVariables]
    public EntityUid?[] Seats = new EntityUid?[2];

    /// <summary>When the balls of the last shot come to rest.</summary>
    [ViewVariables]
    public TimeSpan BusyUntil;
}

/// <summary>A pool cue. One must be in hand to shoot.</summary>
[RegisterComponent]
public sealed partial class WGPoolCueComponent : Component;
