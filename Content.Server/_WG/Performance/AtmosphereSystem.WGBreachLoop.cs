using Content.Server.Atmos.Components;
using Content.Server.Chat.Managers;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    [Dependency] private IChatManager _wgChat = default!;

    /// <summary>A gap this long between two depressurizations from one tile ends its run.</summary>
    private static readonly TimeSpan WGBreachGap = TimeSpan.FromSeconds(5);

    /// <summary>How long one tile may keep depressurizing before it counts as a refill loop; a real breach drains well within it.</summary>
    private static readonly TimeSpan WGBreachLoopTime = TimeSpan.FromMinutes(3);

    /// <summary>How long a looping tile leaks through ordinary gas sharing instead of space wind.</summary>
    private static readonly TimeSpan WGBreachQuietTime = TimeSpan.FromMinutes(10);

    /// <summary>Runs of explosive depressurization per grid and starting tile.</summary>
    private readonly Dictionary<(EntityUid Grid, Vector2i Tile), WGBreachRun> _wgBreachRuns = new();

    private TimeSpan _wgNextBreachPrune;

    private sealed class WGBreachRun
    {
        public TimeSpan Start;
        public TimeSpan Last;
        public TimeSpan QuietUntil;
        public bool Alerted;
    }

    /// <summary>
    /// Whether a depressurization from this tile should be skipped because the room behind it keeps refilling into a
    /// hole: the same tile venting nonstop throws items, plays wind and resends physics to everyone near it forever.
    /// The room still leaks to space through normal gas sharing while skipped. Public for tests.
    /// </summary>
    public bool WGBreachLooping(EntityUid grid, TileAtmosphere tile)
    {
        var now = _gameTiming.CurTime;
        WGPruneBreachRuns(now);

        var key = (grid, tile.GridIndices);
        if (!_wgBreachRuns.TryGetValue(key, out var run))
        {
            _wgBreachRuns[key] = new WGBreachRun { Start = now, Last = now };
            return false;
        }

        // A loop still going when its quiet ends goes straight back to quiet. It alerts once per run, not every ten minutes.
        if (now < run.QuietUntil)
        {
            run.Last = now;
            return true;
        }

        if (now - run.Last > WGBreachGap)
            run.Start = now;
        run.Last = now;

        if (now - run.Start < WGBreachLoopTime)
            return false;

        run.Start = now;
        run.QuietUntil = now + WGBreachQuietTime;
        if (run.Alerted)
            return true;

        run.Alerted = true;
        var name = ToPrettyString(grid);
        Log.Info($"Explosive depressurization from {tile.GridIndices} on {name} has looped for {WGBreachLoopTime.TotalMinutes} minutes; quieting it.");
        _wgChat.SendAdminAlert(Loc.GetString("wg-atmos-breach-loop-alert",
            ("grid", name.ToString()), ("x", tile.GridIndices.X), ("y", tile.GridIndices.Y),
            ("minutes", WGBreachLoopTime.TotalMinutes)));
        return true;
    }

    /// <summary>
    /// Marks the tiles a quieted zone search reached as done this cycle, so the room's other active tiles don't repeat
    /// the search. Gas sharing between them is untouched.
    /// </summary>
    private void WGMarkEqualized(int tileCount, int cycleNum)
    {
        for (var i = 0; i < tileCount; i++)
        {
            if (_equalizeTiles[i] is { } tile)
                tile.MonstermosInfo.LastCycle = cycleNum;
        }
    }

    /// <summary>Drops runs that ended and are no longer quieted, once a minute.</summary>
    private void WGPruneBreachRuns(TimeSpan now)
    {
        if (now < _wgNextBreachPrune)
            return;

        _wgNextBreachPrune = now + TimeSpan.FromMinutes(1);
        foreach (var (key, run) in _wgBreachRuns)
        {
            if (now - run.Last > WGBreachGap && now >= run.QuietUntil)
                _wgBreachRuns.Remove(key);
        }
    }
}
