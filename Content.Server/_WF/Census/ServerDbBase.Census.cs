using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WF.Census;
using Content.Shared._WF.Census;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

// Censuses and the players' answers.
public abstract partial class ServerDbBase
{
    public async Task<int> CloseElapsedCensusesAsync(DateTime now, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        return await db.DbContext.WolfgateCensus
            .Where(c => c.State == CensusRules.StateOpen && c.ClosesAt != null && c.ClosesAt <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.State, CensusRules.StateClosed)
                .SetProperty(c => c.ClosedAt, c => c.ClosesAt), cancel);
    }

    public async Task<List<CensusRow>> GetCensusesAsync(bool openOnly, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var rows = await db.DbContext.WolfgateCensus
            .AsNoTracking()
            .Where(c => !openOnly || c.State == CensusRules.StateOpen)
            .OrderByDescending(c => c.Id)
            .Select(c => new { Census = c, Responses = c.Responses.Count })
            .ToListAsync(cancel);

        return rows.Select(r => new CensusRow(NormalizeTimes(r.Census), r.Responses)).ToList();
    }

    public async Task<CensusRow?> GetCensusAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var row = await db.DbContext.WolfgateCensus
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { Census = c, Responses = c.Responses.Count })
            .SingleOrDefaultAsync(cancel);

        return row == null ? null : new CensusRow(NormalizeTimes(row.Census), row.Responses);
    }

    public async Task<int> SaveCensusAsync(WolfgateCensus census, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        if (census.Id == 0)
            db.DbContext.WolfgateCensus.Add(census);
        else
            db.DbContext.WolfgateCensus.Update(census);

        await db.DbContext.SaveChangesAsync(cancel);
        return census.Id;
    }

    public async Task<bool> DeleteCensusAsync(int id, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        // Answers first: SQLite only cascades with foreign keys switched on.
        await db.DbContext.WolfgateCensusResponse.Where(r => r.CensusId == id).ExecuteDeleteAsync(cancel);
        return await db.DbContext.WolfgateCensus.Where(c => c.Id == id).ExecuteDeleteAsync(cancel) > 0;
    }

    public async Task<HashSet<int>> GetCensusesAnsweredAsync(Guid userId, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        var ids = await db.DbContext.WolfgateCensusResponse
            .Where(r => r.PlayerUserId == userId)
            .Select(r => r.CensusId)
            .ToListAsync(cancel);

        return ids.ToHashSet();
    }

    public async Task<bool> AddCensusResponseAsync(WolfgateCensusResponse response, CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);

        if (await db.DbContext.WolfgateCensusResponse.AnyAsync(r => r.CensusId == response.CensusId && r.PlayerUserId == response.PlayerUserId, cancel))
            return false;

        db.DbContext.WolfgateCensusResponse.Add(response);
        try
        {
            await db.DbContext.SaveChangesAsync(cancel);
        }
        catch (DbUpdateException)
        {
            // Another answer won the race to the unique index.
            return false;
        }

        return true;
    }

    private WolfgateCensus NormalizeTimes(WolfgateCensus census)
    {
        census.CreatedAt = NormalizeDatabaseTime(census.CreatedAt);
        census.OpenedAt = NormalizeDatabaseTime(census.OpenedAt);
        census.ClosesAt = NormalizeDatabaseTime(census.ClosesAt);
        census.ClosedAt = NormalizeDatabaseTime(census.ClosedAt);
        return census;
    }
}
