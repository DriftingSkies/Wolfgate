using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>One saved outpost of an account, without its grid.</summary>
public sealed record WFStoredOutpostSave(
    int Slot,
    string Name,
    int Tiles,
    int Entities,
    int Appraisal,
    int SavedRound,
    int LoadedRound,
    DateTime Saved,
    Guid Lineage);

public partial interface IServerDbManager
{
    /// <summary>Every outpost an account has saved, without their grids.</summary>
    Task<List<WFStoredOutpostSave>> GetOutpostSavesAsync(Guid userId, CancellationToken cancel = default);

    /// <summary>One saved outpost with its gzip'd grid, or null if the slot is empty.</summary>
    Task<(WFStoredOutpostSave Save, byte[] Data)?> GetOutpostSaveDataAsync(Guid userId, int slot, CancellationToken cancel = default);

    /// <summary>Writes an outpost into a slot, replacing what was there.</summary>
    Task SetOutpostSaveAsync(Guid userId, WFStoredOutpostSave save, byte[] data, CancellationToken cancel = default);

    /// <summary>Notes the round a saved outpost was loaded in.</summary>
    Task SetOutpostSaveLoadedAsync(Guid userId, int slot, int round, CancellationToken cancel = default);

    /// <summary>Empties a slot.</summary>
    Task DeleteOutpostSaveAsync(Guid userId, int slot, CancellationToken cancel = default);

    /// <summary>Deletes every save of an outpost, in whoever's slots, once it is sold.</summary>
    Task VoidOutpostLineageAsync(Guid lineage, CancellationToken cancel = default);

    /// <summary>Owes an offline owner what their outpost fetched at round end.</summary>
    Task AddOutpostPayoutAsync(Guid userId, string name, int amount, CancellationToken cancel = default);

    /// <summary>Everything an account is owed for outposts sold at round end, cleared as it is read.</summary>
    Task<List<(string Name, int Amount)>> TakeOutpostPayoutsAsync(Guid userId, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<WFStoredOutpostSave>> GetOutpostSavesAsync(Guid userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetOutpostSavesAsync(userId, cancel));
    }

    public Task<(WFStoredOutpostSave Save, byte[] Data)?> GetOutpostSaveDataAsync(Guid userId, int slot, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetOutpostSaveDataAsync(userId, slot, cancel));
    }

    // Writes go through the account's character write queue, so they never overlap a character save.
    public Task SetOutpostSaveAsync(Guid userId, WFStoredOutpostSave save, byte[] data, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.SetOutpostSaveAsync(userId, save, data, cancel)));
    }

    public Task SetOutpostSaveLoadedAsync(Guid userId, int slot, int round, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.SetOutpostSaveLoadedAsync(userId, slot, round, cancel)));
    }

    public Task DeleteOutpostSaveAsync(Guid userId, int slot, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.DeleteOutpostSaveAsync(userId, slot, cancel)));
    }

    public Task VoidOutpostLineageAsync(Guid lineage, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.VoidOutpostLineageAsync(lineage, cancel));
    }

    public Task AddOutpostPayoutAsync(Guid userId, string name, int amount, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.AddOutpostPayoutAsync(userId, name, amount, cancel)));
    }

    public Task<List<(string Name, int Amount)>> TakeOutpostPayoutsAsync(Guid userId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.TakeOutpostPayoutsAsync(userId, cancel));
    }
}

public abstract partial class ServerDbBase
{
    public async Task<List<WFStoredOutpostSave>> GetOutpostSavesAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateOutpostSave
            .Where(s => s.PlayerUserId == userId)
            .OrderBy(s => s.Slot)
            .Select(s => new WFStoredOutpostSave(s.Slot, s.Name, s.Tiles, s.Entities, s.Appraisal, s.SavedRound, s.LoadedRound, s.Saved, s.Lineage))
            .ToListAsync(cancel);
    }

    public async Task<(WFStoredOutpostSave Save, byte[] Data)?> GetOutpostSaveDataAsync(Guid userId, int slot, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var s = await db.DbContext.WolfgateOutpostSave
            .SingleOrDefaultAsync(s => s.PlayerUserId == userId && s.Slot == slot, cancel);
        if (s == null)
            return null;

        return (new WFStoredOutpostSave(s.Slot, s.Name, s.Tiles, s.Entities, s.Appraisal, s.SavedRound, s.LoadedRound, s.Saved, s.Lineage), s.Data);
    }

    public async Task SetOutpostSaveAsync(Guid userId, WFStoredOutpostSave save, byte[] data, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateOutpostSave
            .SingleOrDefaultAsync(s => s.PlayerUserId == userId && s.Slot == save.Slot, cancel);

        if (entry == null)
        {
            entry = new WolfgateOutpostSave { PlayerUserId = userId, Slot = save.Slot };
            db.DbContext.WolfgateOutpostSave.Add(entry);
        }

        entry.Name = save.Name;
        entry.Tiles = save.Tiles;
        entry.Entities = save.Entities;
        entry.Appraisal = save.Appraisal;
        entry.Data = data;
        entry.SavedRound = save.SavedRound;
        entry.LoadedRound = save.LoadedRound;
        entry.Saved = save.Saved;
        entry.Lineage = save.Lineage;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task SetOutpostSaveLoadedAsync(Guid userId, int slot, int round, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateOutpostSave
            .SingleOrDefaultAsync(s => s.PlayerUserId == userId && s.Slot == slot, cancel);
        if (entry == null)
            return;

        entry.LoadedRound = round;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DeleteOutpostSaveAsync(Guid userId, int slot, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateOutpostSave
            .SingleOrDefaultAsync(s => s.PlayerUserId == userId && s.Slot == slot, cancel);
        if (entry == null)
            return;

        db.DbContext.WolfgateOutpostSave.Remove(entry);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task VoidOutpostLineageAsync(Guid lineage, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var saves = await db.DbContext.WolfgateOutpostSave.Where(s => s.Lineage == lineage).ToListAsync(cancel);
        db.DbContext.WolfgateOutpostSave.RemoveRange(saves);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task AddOutpostPayoutAsync(Guid userId, string name, int amount, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        db.DbContext.WolfgateOutpostPayout.Add(new WolfgateOutpostPayout
        {
            PlayerUserId = userId,
            Name = name.Length > 64 ? name[..64] : name,
            Amount = amount,
            Sold = DateTime.UtcNow,
        });
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<List<(string Name, int Amount)>> TakeOutpostPayoutsAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var payouts = await db.DbContext.WolfgateOutpostPayout.Where(p => p.PlayerUserId == userId).ToListAsync(cancel);
        db.DbContext.WolfgateOutpostPayout.RemoveRange(payouts);
        await db.DbContext.SaveChangesAsync(cancel);
        return payouts.Select(p => (p.Name, p.Amount)).ToList();
    }
}
