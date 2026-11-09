using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>A holder's development score on a world.</summary>
public sealed record WFStoredDevelopment(string Holder, string World, string Name, double Points, DateTime Updated);

/// <summary>A world's charter.</summary>
public sealed record WFStoredCharter(
    string World,
    int CompanyId,
    DateTime Granted,
    int? Rival,
    int Streak,
    DateTime LastCheck,
    bool Defence,
    bool Open,
    string Permits);

/// <summary>A world's yard platform, without its grid.</summary>
public sealed record WFStoredYard(string World, float X, float Y, string Berths, DateTime Saved);

/// <summary>A build on a world's yard.</summary>
public sealed record WFStoredYardJob(int Id, string World, int CompanyId, string Design, int Stage, float Progress, int Components, int Metals);

public partial interface IServerDbManager
{
    /// <summary>Every holder's score on every world.</summary>
    Task<List<WFStoredDevelopment>> GetWorldDevelopmentAsync(CancellationToken cancel = default);

    /// <summary>Writes holders' scores, adding rows for new ones.</summary>
    Task SetWorldDevelopmentAsync(List<WFStoredDevelopment> rows, CancellationToken cancel = default);

    /// <summary>Every world's charter.</summary>
    Task<List<WFStoredCharter>> GetWorldChartersAsync(CancellationToken cancel = default);

    /// <summary>Writes a world's charter, or ends it when <paramref name="charter"/> is null.</summary>
    Task SetWorldCharterAsync(string world, WFStoredCharter? charter, CancellationToken cancel = default);

    /// <summary>Every world's yard platform with its gzip'd grid.</summary>
    Task<List<(WFStoredYard Yard, byte[] Data)>> GetWorldYardsAsync(CancellationToken cancel = default);

    /// <summary>Writes a world's yard platform; a null grid keeps the stored one and only writes the rest.</summary>
    Task SetWorldYardAsync(WFStoredYard yard, byte[]? data, CancellationToken cancel = default);

    /// <summary>Removes a world's yard platform.</summary>
    Task DeleteWorldYardAsync(string world, CancellationToken cancel = default);

    /// <summary>Every build on every yard.</summary>
    Task<List<WFStoredYardJob>> GetYardJobsAsync(CancellationToken cancel = default);

    /// <summary>Writes a build, returning its id; a new build has id 0.</summary>
    Task<int> SetYardJobAsync(WFStoredYardJob job, CancellationToken cancel = default);

    /// <summary>Removes a finished or scrapped build.</summary>
    Task DeleteYardJobAsync(int id, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<WFStoredDevelopment>> GetWorldDevelopmentAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetWorldDevelopmentAsync(cancel));
    }

    public Task SetWorldDevelopmentAsync(List<WFStoredDevelopment> rows, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetWorldDevelopmentAsync(rows, cancel));
    }

    public Task<List<WFStoredCharter>> GetWorldChartersAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetWorldChartersAsync(cancel));
    }

    public Task SetWorldCharterAsync(string world, WFStoredCharter? charter, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetWorldCharterAsync(world, charter, cancel));
    }

    public Task<List<(WFStoredYard Yard, byte[] Data)>> GetWorldYardsAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetWorldYardsAsync(cancel));
    }

    public Task SetWorldYardAsync(WFStoredYard yard, byte[]? data, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetWorldYardAsync(yard, data, cancel));
    }

    public Task DeleteWorldYardAsync(string world, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteWorldYardAsync(world, cancel));
    }

    public Task<List<WFStoredYardJob>> GetYardJobsAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetYardJobsAsync(cancel));
    }

    public Task<int> SetYardJobAsync(WFStoredYardJob job, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetYardJobAsync(job, cancel));
    }

    public Task DeleteYardJobAsync(int id, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteYardJobAsync(id, cancel));
    }
}

public abstract partial class ServerDbBase
{
    public async Task<List<WFStoredDevelopment>> GetWorldDevelopmentAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateWorldDevelopment
            .Select(d => new WFStoredDevelopment(d.Holder, d.World, d.Name, d.Points, d.Updated))
            .ToListAsync(cancel);
    }

    public async Task SetWorldDevelopmentAsync(List<WFStoredDevelopment> rows, CancellationToken cancel = default)
    {
        if (rows.Count == 0)
            return;

        await using var db = await GetDb(cancel);
        var worlds = rows.Select(r => r.World).Distinct().ToList();
        var existing = await db.DbContext.WolfgateWorldDevelopment
            .Where(d => worlds.Contains(d.World))
            .ToListAsync(cancel);
        var byKey = existing.ToDictionary(d => (d.World, d.Holder));
        foreach (var row in rows)
        {
            if (!byKey.TryGetValue((row.World, row.Holder), out var entry))
            {
                entry = new WolfgateWorldDevelopment { World = row.World, Holder = row.Holder };
                db.DbContext.WolfgateWorldDevelopment.Add(entry);
                byKey[(row.World, row.Holder)] = entry;
            }

            entry.Name = row.Name.Length > 64 ? row.Name[..64] : row.Name;
            entry.Points = row.Points;
            entry.Updated = row.Updated;
        }

        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<List<WFStoredCharter>> GetWorldChartersAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateWorldCharter
            .Select(c => new WFStoredCharter(c.World, c.CompanyId, c.Granted, c.Rival, c.Streak, c.LastCheck, c.Defence, c.Open, c.Permits))
            .ToListAsync(cancel);
    }

    public async Task SetWorldCharterAsync(string world, WFStoredCharter? charter, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateWorldCharter.SingleOrDefaultAsync(c => c.World == world, cancel);
        if (charter == null)
        {
            if (entry != null)
                db.DbContext.WolfgateWorldCharter.Remove(entry);
            await db.DbContext.SaveChangesAsync(cancel);
            return;
        }

        if (entry == null)
        {
            entry = new WolfgateWorldCharter { World = world };
            db.DbContext.WolfgateWorldCharter.Add(entry);
        }

        entry.CompanyId = charter.CompanyId;
        entry.Granted = charter.Granted;
        entry.Rival = charter.Rival;
        entry.Streak = charter.Streak;
        entry.LastCheck = charter.LastCheck;
        entry.Defence = charter.Defence;
        entry.Open = charter.Open;
        entry.Permits = charter.Permits.Length > 2048 ? charter.Permits[..2048] : charter.Permits;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<List<(WFStoredYard Yard, byte[] Data)>> GetWorldYardsAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var yards = await db.DbContext.WolfgateWorldYard.ToListAsync(cancel);
        return yards.Select(y => (new WFStoredYard(y.World, y.X, y.Y, y.Berths, y.Saved), y.Data)).ToList();
    }

    public async Task SetWorldYardAsync(WFStoredYard yard, byte[]? data, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateWorldYard.SingleOrDefaultAsync(y => y.World == yard.World, cancel);
        if (entry == null)
        {
            if (data == null)
                return;

            entry = new WolfgateWorldYard { World = yard.World };
            db.DbContext.WolfgateWorldYard.Add(entry);
        }

        if (data != null)
            entry.Data = data;
        entry.X = yard.X;
        entry.Y = yard.Y;
        entry.Berths = yard.Berths.Length > 2048 ? yard.Berths[..2048] : yard.Berths;
        entry.Saved = yard.Saved;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DeleteWorldYardAsync(string world, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateWorldYard.SingleOrDefaultAsync(y => y.World == world, cancel);
        if (entry == null)
            return;

        db.DbContext.WolfgateWorldYard.Remove(entry);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<List<WFStoredYardJob>> GetYardJobsAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateYardJob
            .Select(j => new WFStoredYardJob(j.Id, j.World, j.CompanyId, j.Design, j.Stage, j.Progress, j.Components, j.Metals))
            .ToListAsync(cancel);
    }

    public async Task<int> SetYardJobAsync(WFStoredYardJob job, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = job.Id == 0 ? null : await db.DbContext.WolfgateYardJob.SingleOrDefaultAsync(j => j.Id == job.Id, cancel);
        if (entry == null)
        {
            entry = new WolfgateYardJob();
            db.DbContext.WolfgateYardJob.Add(entry);
        }

        entry.World = job.World;
        entry.CompanyId = job.CompanyId;
        entry.Design = job.Design;
        entry.Stage = job.Stage;
        entry.Progress = job.Progress;
        entry.Components = job.Components;
        entry.Metals = job.Metals;
        entry.Updated = DateTime.UtcNow;
        await db.DbContext.SaveChangesAsync(cancel);
        return entry.Id;
    }

    public async Task DeleteYardJobAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateYardJob.SingleOrDefaultAsync(j => j.Id == id, cancel);
        if (entry == null)
            return;

        db.DbContext.WolfgateYardJob.Remove(entry);
        await db.DbContext.SaveChangesAsync(cancel);
    }
}
