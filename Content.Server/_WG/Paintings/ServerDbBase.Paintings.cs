using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    /// <summary>Every catalogue painting, blocked ones included, without their pixels.</summary>
    public async Task<List<WolfgatePainting>> GetPaintingIndexAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgatePainting.AsNoTracking()
            .Select(p => new WolfgatePainting
            {
                Hash = p.Hash,
                Width = p.Width,
                Height = p.Height,
                Pixels = Array.Empty<byte>(),
                Title = p.Title,
                Artist = p.Artist,
                AuthorUserId = p.AuthorUserId,
                ScannedByUserId = p.ScannedByUserId,
                CreatedAt = p.CreatedAt,
                Blocked = p.Blocked,
            })
            .ToListAsync(cancel);
    }

    public async Task<byte[]?> GetPaintingPixelsAsync(string hash, bool includeBlocked = false,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgatePainting.AsNoTracking()
            .Where(p => p.Hash == hash && (includeBlocked || !p.Blocked))
            .Select(p => p.Pixels)
            .SingleOrDefaultAsync(cancel);
    }

    /// <summary>The last seen usernames of the given players, for those the database knows.</summary>
    public async Task<Dictionary<Guid, string>> GetPlayerNamesAsync(List<Guid> userIds,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.Player.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, p => p.LastSeenUserName, cancel);
    }

    /// <summary>Adds a painting. False when the hash is already stored, blocked or not.</summary>
    public async Task<bool> AddPaintingAsync(WolfgatePainting painting, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        if (await ctx.WolfgatePainting.AnyAsync(p => p.Hash == painting.Hash, cancel))
            return false;

        ctx.WolfgatePainting.Add(painting);
        await ctx.SaveChangesAsync(cancel);
        return true;
    }

    public async Task DeletePaintingAsync(string hash, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        await db.DbContext.WolfgatePainting.Where(p => p.Hash == hash).ExecuteDeleteAsync(cancel);
    }

    /// <summary>Blocks or unblocks a painting. False when there is no such painting.</summary>
    public async Task<bool> SetPaintingBlockedAsync(string hash, bool blocked, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var changed = await db.DbContext.WolfgatePainting
            .Where(p => p.Hash == hash)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Blocked, blocked), cancel);
        return changed > 0;
    }
}
