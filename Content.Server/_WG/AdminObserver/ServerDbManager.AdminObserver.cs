using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.ProfileSaves;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>An admin's observer document as JSON, or null when they have none.</summary>
    Task<string?> GetAdminObserverDataAsync(Guid userId, CancellationToken cancel = default);

    /// <summary>Saves an admin's observer document, replacing the one they had.</summary>
    Task SaveAdminObserverDataAsync(Guid userId, string data, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    /// <summary>Keeps one admin's observer writes in order, so an older document never lands after a newer one.</summary>
    private readonly WGUserWriteQueue _wgObserverWrites = new();

    public Task<string?> GetAdminObserverDataAsync(Guid userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetAdminObserverDataAsync(userId, cancel));
    }

    public Task SaveAdminObserverDataAsync(Guid userId, string data, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wgObserverWrites.Run(new NetUserId(userId), () => RunDbCommand(() => _db.SaveAdminObserverDataAsync(userId, data, cancel)));
    }
}

public abstract partial class ServerDbBase
{
    public async Task<string?> GetAdminObserverDataAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateAdminObserver
            .Where(o => o.PlayerUserId == userId)
            .Select(o => o.Data)
            .SingleOrDefaultAsync(cancel);
    }

    public async Task SaveAdminObserverDataAsync(Guid userId, string data, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateAdminObserver
            .SingleOrDefaultAsync(o => o.PlayerUserId == userId, cancel);

        if (entry == null)
        {
            entry = new WolfgateAdminObserver { PlayerUserId = userId };
            db.DbContext.WolfgateAdminObserver.Add(entry);
        }

        entry.Data = data;
        entry.Saved = DateTime.UtcNow;
        await db.DbContext.SaveChangesAsync(cancel);
    }
}
