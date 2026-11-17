using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>A stored ship preset's name and when it was saved.</summary>
public sealed record WGStoredShipPreset(string Name, DateTime Saved);

public partial interface IServerDbManager
{
    /// <summary>A player's preset of a name for a vessel design, as JSON, or null.</summary>
    Task<string?> GetShipCommandPresetAsync(Guid userId, string vessel, string name, CancellationToken cancel = default);

    /// <summary>A player's presets for a vessel design, by name.</summary>
    Task<List<WGStoredShipPreset>> GetShipCommandPresetsAsync(Guid userId, string vessel, CancellationToken cancel = default);

    /// <summary>Saves a player's preset for a vessel design, replacing one of the same name.</summary>
    Task SaveShipCommandPresetAsync(Guid userId, string vessel, string name, string data, CancellationToken cancel = default);

    /// <summary>Deletes a player's preset. False when there was none.</summary>
    Task<bool> DeleteShipCommandPresetAsync(Guid userId, string vessel, string name, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<string?> GetShipCommandPresetAsync(Guid userId, string vessel, string name, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetShipCommandPresetAsync(userId, vessel, name, cancel));
    }

    public Task<List<WGStoredShipPreset>> GetShipCommandPresetsAsync(Guid userId, string vessel, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetShipCommandPresetsAsync(userId, vessel, cancel));
    }

    public Task SaveShipCommandPresetAsync(Guid userId, string vessel, string name, string data, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SaveShipCommandPresetAsync(userId, vessel, name, data, cancel));
    }

    public Task<bool> DeleteShipCommandPresetAsync(Guid userId, string vessel, string name, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.DeleteShipCommandPresetAsync(userId, vessel, name, cancel));
    }
}

public abstract partial class ServerDbBase
{
    public async Task<string?> GetShipCommandPresetAsync(Guid userId, string vessel, string name, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateShipCommandPreset
            .Where(p => p.PlayerUserId == userId && p.Vessel == vessel && p.Name == name)
            .Select(p => p.Data)
            .SingleOrDefaultAsync(cancel);
    }

    public async Task<List<WGStoredShipPreset>> GetShipCommandPresetsAsync(Guid userId, string vessel, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateShipCommandPreset
            .Where(p => p.PlayerUserId == userId && p.Vessel == vessel)
            .OrderBy(p => p.Name)
            .Select(p => new WGStoredShipPreset(p.Name, p.Saved))
            .ToListAsync(cancel);
    }

    public async Task SaveShipCommandPresetAsync(Guid userId, string vessel, string name, string data, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateShipCommandPreset
            .SingleOrDefaultAsync(p => p.PlayerUserId == userId && p.Vessel == vessel && p.Name == name, cancel);

        if (entry == null)
        {
            entry = new WolfgateShipCommandPreset { PlayerUserId = userId, Vessel = vessel, Name = name };
            db.DbContext.WolfgateShipCommandPreset.Add(entry);
        }

        entry.Data = data;
        entry.Saved = DateTime.UtcNow;
        await db.DbContext.SaveChangesAsync(cancel);
    }

    public async Task<bool> DeleteShipCommandPresetAsync(Guid userId, string vessel, string name, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var entry = await db.DbContext.WolfgateShipCommandPreset
            .SingleOrDefaultAsync(p => p.PlayerUserId == userId && p.Vessel == vessel && p.Name == name, cancel);

        if (entry == null)
            return false;

        db.DbContext.WolfgateShipCommandPreset.Remove(entry);
        await db.DbContext.SaveChangesAsync(cancel);
        return true;
    }
}
