using Content.Shared.Administration.Logs;
using Robust.Shared.Configuration;

namespace Content.Server.Administration.Logs;

/// <summary>Server cvars for capping the in-memory admin log cache.</summary>
[CVarDefs]
public sealed class WGAdminLogCacheCVars
{
    /// <summary>
    /// Most admin logs a round keeps in memory. Past it the round's cache is dropped and its searches go to the database,
    /// which has every log; 0 keeps them all, as upstream does. Every log of a long round stayed in memory three rounds deep.
    /// </summary>
    public static readonly CVarDef<int> CacheMax =
        CVarDef.Create("wg.admin_logs.cache_max", 250_000, CVar.SERVERONLY);
}

public sealed partial class AdminLogManager
{
    /// <summary>Rounds whose cache was dropped at the cap; they are searched in the database instead.</summary>
    private readonly HashSet<int> _wgUncachedRounds = new();

    /// <summary>Whether a round's logs are searched in memory, as opposed to in the database.</summary>
    public bool WGIsRoundCached(int roundId) => !_wgUncachedRounds.Contains(roundId) && _roundsLogCache.ContainsKey(roundId);

    /// <summary>Whether the current round's cache has been dropped, so nothing more is added to it.</summary>
    private bool WGCacheDropped() => _wgUncachedRounds.Contains(_currentRoundId);

    /// <summary>Drops the round's cache once it passes <see cref="WGAdminLogCacheCVars.CacheMax"/>.</summary>
    private void WGCheckCacheCap(List<SharedAdminLog> cache)
    {
        var max = _configuration.GetCVar(WGAdminLogCacheCVars.CacheMax);
        if (max <= 0 || cache.Count <= max)
            return;

        _sawmill.Info($"Round {_currentRoundId} passed {max} cached admin logs; its searches go to the database from now on.");
        cache.Clear();
        cache.TrimExcess();
        _wgUncachedRounds.Add(_currentRoundId);
        CacheLogCount.Set(0);
    }

    /// <summary>Forgets dropped rounds that are no longer in the cache queue.</summary>
    private void WGForgetUncachedRounds()
    {
        _wgUncachedRounds.RemoveWhere(round => !_roundsLogCache.ContainsKey(round) && round != _currentRoundId);
    }
}
