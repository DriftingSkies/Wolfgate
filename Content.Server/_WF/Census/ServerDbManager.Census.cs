using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Census;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>Closes the open censuses whose closes_at has passed, closed at that time. Returns how many.</summary>
    Task<int> CloseElapsedCensusesAsync(DateTime now, CancellationToken cancel = default);

    /// <summary>Censuses with their answer counts, newest first.</summary>
    Task<List<CensusRow>> GetCensusesAsync(bool openOnly, CancellationToken cancel = default);

    /// <summary>One census with its answer count, or null.</summary>
    Task<CensusRow?> GetCensusAsync(int id, CancellationToken cancel = default);

    /// <summary>Adds a census (id 0) or replaces its row. Returns its id.</summary>
    Task<int> SaveCensusAsync(WolfgateCensus census, CancellationToken cancel = default);

    /// <summary>Deletes a census and its answers. False when there was none.</summary>
    Task<bool> DeleteCensusAsync(int id, CancellationToken cancel = default);

    /// <summary>The ids of the censuses a player has answered.</summary>
    Task<HashSet<int>> GetCensusesAnsweredAsync(Guid userId, CancellationToken cancel = default);

    /// <summary>Stores a player's answers. False when they have already answered this census.</summary>
    Task<bool> AddCensusResponseAsync(WolfgateCensusResponse response, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<int> CloseElapsedCensusesAsync(DateTime now, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.CloseElapsedCensusesAsync(now, cancel));
    }

    public Task<List<CensusRow>> GetCensusesAsync(bool openOnly, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetCensusesAsync(openOnly, cancel));
    }

    public Task<CensusRow?> GetCensusAsync(int id, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetCensusAsync(id, cancel));
    }

    public Task<int> SaveCensusAsync(WolfgateCensus census, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveCensusAsync(census, cancel));
    }

    public Task<bool> DeleteCensusAsync(int id, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteCensusAsync(id, cancel));
    }

    public Task<HashSet<int>> GetCensusesAnsweredAsync(Guid userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetCensusesAnsweredAsync(userId, cancel));
    }

    public Task<bool> AddCensusResponseAsync(WolfgateCensusResponse response, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddCensusResponseAsync(response, cancel));
    }
}
