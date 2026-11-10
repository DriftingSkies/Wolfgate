using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>An account's hangar ship, without its grids.</summary>
public sealed record WFStoredHideoutShip(
    string Name,
    int Value,
    int SavedRound,
    DateTime Saved,
    bool Lost,
    int InsuredValue,
    bool Insured);

public partial interface IServerDbManager
{
    /// <summary>An account's hangar ship without its grids, or null if it has none.</summary>
    Task<WFStoredHideoutShip?> GetHideoutShipAsync(Guid userId, CancellationToken cancel = default);

    /// <summary>An account's hangar ship with its packed grid, as it last landed or as it was insured.</summary>
    Task<(WFStoredHideoutShip Ship, byte[] Data)?> GetHideoutShipDataAsync(Guid userId, bool insured, CancellationToken cancel = default);

    /// <summary>Writes the ship as it landed, replacing the last landing; it is no longer lost.</summary>
    Task SetHideoutShipAsync(Guid userId, string name, int value, int round, byte[] data, CancellationToken cancel = default);

    /// <summary>Writes the ship's insured state and what it was worth.</summary>
    Task SetHideoutShipInsuredAsync(Guid userId, int value, byte[] data, CancellationToken cancel = default);

    /// <summary>Notes whether the ship was destroyed.</summary>
    Task SetHideoutShipLostAsync(Guid userId, bool lost, CancellationToken cancel = default);

    /// <summary>Makes the insured state the ship's last landing and uses up the policy.</summary>
    Task ClaimHideoutShipAsync(Guid userId, int round, CancellationToken cancel = default);

    /// <summary>Forgets the account's hangar ship and its policy.</summary>
    Task DeleteHideoutShipAsync(Guid userId, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<WFStoredHideoutShip?> GetHideoutShipAsync(Guid userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetHideoutShipAsync(userId, cancel));
    }

    public Task<(WFStoredHideoutShip Ship, byte[] Data)?> GetHideoutShipDataAsync(Guid userId, bool insured, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetHideoutShipDataAsync(userId, insured, cancel));
    }

    // Writes go through the account's character write queue, like outpost saves.
    public Task SetHideoutShipAsync(Guid userId, string name, int value, int round, byte[] data, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.SetHideoutShipAsync(userId, name, value, round, data, cancel)));
    }

    public Task SetHideoutShipInsuredAsync(Guid userId, int value, byte[] data, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.SetHideoutShipInsuredAsync(userId, value, data, cancel)));
    }

    public Task SetHideoutShipLostAsync(Guid userId, bool lost, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.SetHideoutShipLostAsync(userId, lost, cancel)));
    }

    public Task ClaimHideoutShipAsync(Guid userId, int round, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.ClaimHideoutShipAsync(userId, round, cancel)));
    }

    public Task DeleteHideoutShipAsync(Guid userId, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return _wfProfileWrites.Run(new(userId), () => RunDbCommand(() => _db.DeleteHideoutShipAsync(userId, cancel)));
    }
}

public abstract partial class ServerDbBase
{
    private static WFStoredHideoutShip HideoutShip(WolfgateHideoutShip s)
    {
        return new WFStoredHideoutShip(s.Name, s.Value, s.SavedRound, s.Saved, s.Lost, s.InsuredValue, s.InsuredData != null);
    }

    public async Task<WFStoredHideoutShip?> GetHideoutShipAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateHideoutShip
            .Where(s => s.PlayerUserId == userId)
            .Select(s => new WFStoredHideoutShip(s.Name, s.Value, s.SavedRound, s.Saved, s.Lost, s.InsuredValue, s.InsuredData != null))
            .SingleOrDefaultAsync(cancel);
    }

    public async Task<(WFStoredHideoutShip Ship, byte[] Data)?> GetHideoutShipDataAsync(Guid userId, bool insured, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var s = await db.DbContext.WolfgateHideoutShip.SingleOrDefaultAsync(s => s.PlayerUserId == userId, cancel);
        var data = insured ? s?.InsuredData : s?.Data;
        if (s == null || data == null)
            return null;

        return (HideoutShip(s), data);
    }

    public async Task SetHideoutShipAsync(Guid userId, string name, int value, int round, byte[] data, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateHideoutShip.SingleOrDefaultAsync(s => s.PlayerUserId == userId, cancel);
        if (entry == null)
        {
            entry = new WolfgateHideoutShip { PlayerUserId = userId };
            db.DbContext.WolfgateHideoutShip.Add(entry);
        }

        entry.Name = name.Length > 64 ? name[..64] : name;
        entry.Value = value;
        entry.Data = data;
        entry.SavedRound = round;
        entry.Saved = DateTime.UtcNow;
        entry.Lost = false;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task SetHideoutShipInsuredAsync(Guid userId, int value, byte[] data, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateHideoutShip.SingleOrDefaultAsync(s => s.PlayerUserId == userId, cancel);
        if (entry == null)
            return;

        entry.InsuredValue = value;
        entry.InsuredData = data;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task SetHideoutShipLostAsync(Guid userId, bool lost, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateHideoutShip.SingleOrDefaultAsync(s => s.PlayerUserId == userId, cancel);
        if (entry == null)
            return;

        entry.Lost = lost;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task ClaimHideoutShipAsync(Guid userId, int round, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateHideoutShip.SingleOrDefaultAsync(s => s.PlayerUserId == userId, cancel);
        if (entry?.InsuredData == null)
            return;

        entry.Data = entry.InsuredData;
        entry.Value = entry.InsuredValue;
        entry.InsuredData = null;
        entry.InsuredValue = 0;
        entry.SavedRound = round;
        entry.Saved = DateTime.UtcNow;
        entry.Lost = false;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task DeleteHideoutShipAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateHideoutShip.SingleOrDefaultAsync(s => s.PlayerUserId == userId, cancel);
        if (entry == null)
            return;

        db.DbContext.WolfgateHideoutShip.Remove(entry);
        await db.DbContext.SaveChangesAsync(cancel);
    }
}
