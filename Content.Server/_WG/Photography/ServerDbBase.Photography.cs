using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    /// <summary>How many photo rows one statement of the cleanup names.</summary>
    private const int PhotoPurgeBatch = 200;

    /// <summary>A photo hash as it appears in a saved entity.</summary>
    private static readonly Regex PhotoHashPattern = new("[0-9A-F]{64}", RegexOptions.Compiled);

    public async Task<WolfgatePhoto?> GetPhotoAsync(string hash, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgatePhoto.AsNoTracking().SingleOrDefaultAsync(p => p.Hash == hash, cancel);
    }

    public async Task DeletePhotoAsync(string hash, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        await db.DbContext.WolfgatePhoto.Where(p => p.Hash == hash).ExecuteDeleteAsync(cancel);
    }

    /// <summary>Adds an image, or marks an existing one as in use again.</summary>
    public async Task AddPhotoAsync(WolfgatePhoto photo, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;

        if (await ctx.WolfgatePhoto.AnyAsync(p => p.Hash == photo.Hash, cancel))
        {
            await ctx.WolfgatePhoto
                .Where(p => p.Hash == photo.Hash && p.UnusedSince != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.UnusedSince, (DateTime?) null), cancel);
            return;
        }

        ctx.WolfgatePhoto.Add(photo);
        await ctx.SaveChangesAsync(cancel);
    }

    public async Task<List<WolfgatePhotoAlbum>> GetPhotoAlbumsAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgatePhotoAlbum.AsNoTracking()
            .Where(a => a.Profile.Preference.UserId == userId)
            .OrderByDescending(a => a.UpdatedAt)
            .ToListAsync(cancel);
    }

    /// <summary>Adds or replaces an album's save. Skips it if its character was deleted.</summary>
    public async Task SavePhotoAlbumAsync(WolfgatePhotoAlbum album, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;

        var row = await ctx.WolfgatePhotoAlbum.SingleOrDefaultAsync(a => a.AlbumId == album.AlbumId, cancel);
        if (row == null)
        {
            if (!await ctx.Profile.AnyAsync(p => p.Id == album.ProfileId, cancel))
                return;

            ctx.WolfgatePhotoAlbum.Add(album);
        }
        else
        {
            row.ProfileId = album.ProfileId;
            row.Data = album.Data;
            row.RoundId = album.RoundId;
            row.UpdatedAt = album.UpdatedAt;
        }

        await ctx.SaveChangesAsync(cancel);
    }

    /// <summary>Marks images nothing saved holds, and deletes ones marked longer ago than keep.</summary>
    public async Task<(int Found, int Deleted)> PurgeUnusedPhotosAsync(TimeSpan keep,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;
        var now = DateTime.UtcNow;

        // Carried-over photos are saved entities (albums, profile items, deposit boxes) that name their image by hash.
        var held = new HashSet<string>();
        var saved = await ctx.Profile
            .SelectMany(p => p.Items)
            .Where(i => i.Data.Contains("Photo"))
            .Select(i => i.Data)
            .ToListAsync(cancel);
        saved.AddRange(await ctx.WayfarerSafetyDepositBoxItem
            .Where(i => i.EntityData.Contains("Photo"))
            .Select(i => i.EntityData)
            .ToListAsync(cancel));
        saved.AddRange(await ctx.WolfgatePhotoAlbum.Select(a => a.Data).ToListAsync(cancel));

        foreach (var data in saved)
        {
            foreach (Match match in PhotoHashPattern.Matches(data))
            {
                held.Add(match.Value);
            }
        }

        var rows = await ctx.WolfgatePhoto
            .Select(p => new { p.Hash, p.UnusedSince })
            .ToListAsync(cancel);

        var used = new List<string>();
        var found = new List<string>();
        var old = new List<string>();
        foreach (var row in rows)
        {
            if (held.Contains(row.Hash))
            {
                if (row.UnusedSince != null)
                    used.Add(row.Hash);
            }
            else if (row.UnusedSince is not { } since)
            {
                found.Add(row.Hash);
            }
            else if (since <= now - keep)
            {
                old.Add(row.Hash);
            }
        }

        foreach (var batch in used.Chunk(PhotoPurgeBatch))
        {
            await ctx.WolfgatePhoto
                .Where(p => batch.Contains(p.Hash))
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.UnusedSince, (DateTime?) null), cancel);
        }

        foreach (var batch in found.Chunk(PhotoPurgeBatch))
        {
            await ctx.WolfgatePhoto
                .Where(p => batch.Contains(p.Hash))
                .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.UnusedSince, (DateTime?) now), cancel);
        }

        var deleted = 0;
        foreach (var batch in old.Chunk(PhotoPurgeBatch))
        {
            deleted += await ctx.WolfgatePhoto
                .Where(p => batch.Contains(p.Hash))
                .ExecuteDeleteAsync(cancel);
        }

        return (found.Count, deleted);
    }
}
