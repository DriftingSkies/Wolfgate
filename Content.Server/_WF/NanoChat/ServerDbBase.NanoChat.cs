using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<List<int>> GetNanoChatNumbersAsync(CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.WolfgateNanoChatNumber.AsNoTracking().Select(n => n.Number).ToListAsync(cancel);
    }

    /// <summary>The character's number; a character without one takes the candidate. Null when the candidate is taken.</summary>
    public async Task<int?> ClaimNanoChatNumberAsync(int profileId, int candidate, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var ctx = db.DbContext;

        var existing = await ctx.WolfgateNanoChatNumber.AsNoTracking()
            .Where(n => n.ProfileId == profileId)
            .Select(n => (int?) n.Number)
            .SingleOrDefaultAsync(cancel);
        if (existing != null)
            return existing;

        if (await ctx.WolfgateNanoChatNumber.AnyAsync(n => n.Number == candidate, cancel)
            || !await ctx.Profile.AnyAsync(p => p.Id == profileId, cancel))
            return null;

        ctx.WolfgateNanoChatNumber.Add(new WolfgateNanoChatNumber { ProfileId = profileId, Number = candidate });
        try
        {
            await ctx.SaveChangesAsync(cancel);
        }
        catch (DbUpdateException)
        {
            // Another server took the number, or gave this character one, in the meantime.
            return null;
        }

        return candidate;
    }
}
