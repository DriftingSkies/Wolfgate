using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>Every catalogue painting, blocked ones included, without their pixels.</summary>
    Task<List<WolfgatePainting>> GetPaintingIndexAsync(CancellationToken cancel = default);

    /// <summary>A catalogue painting's pixels, or null when there is none or it is blocked and not asked for.</summary>
    Task<byte[]?> GetPaintingPixelsAsync(string hash, bool includeBlocked = false, CancellationToken cancel = default);

    /// <summary>The last seen usernames of the given players, for those the database knows.</summary>
    Task<Dictionary<Guid, string>> GetPlayerNamesAsync(List<Guid> userIds, CancellationToken cancel = default);

    /// <summary>Adds a painting to the catalogue. False when its hash is already stored.</summary>
    Task<bool> AddPaintingAsync(WolfgatePainting painting, CancellationToken cancel = default);

    /// <summary>Takes a painting out of the catalogue.</summary>
    Task DeletePaintingAsync(string hash, CancellationToken cancel = default);

    /// <summary>Blocks or unblocks a painting. False when there is no such painting.</summary>
    Task<bool> SetPaintingBlockedAsync(string hash, bool blocked, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<WolfgatePainting>> GetPaintingIndexAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetPaintingIndexAsync(cancel));
    }

    public Task<byte[]?> GetPaintingPixelsAsync(string hash, bool includeBlocked = false,
        CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetPaintingPixelsAsync(hash, includeBlocked, cancel));
    }

    public Task<Dictionary<Guid, string>> GetPlayerNamesAsync(List<Guid> userIds, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetPlayerNamesAsync(userIds, cancel));
    }

    public Task<bool> AddPaintingAsync(WolfgatePainting painting, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.AddPaintingAsync(painting, cancel));
    }

    public Task DeletePaintingAsync(string hash, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeletePaintingAsync(hash, cancel));
    }

    public Task<bool> SetPaintingBlockedAsync(string hash, bool blocked, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetPaintingBlockedAsync(hash, blocked, cancel));
    }
}
