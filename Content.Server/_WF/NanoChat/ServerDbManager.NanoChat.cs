using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /// <summary>Every number saved for a character.</summary>
    Task<List<int>> GetNanoChatNumbersAsync(CancellationToken cancel = default);

    /// <summary>The character's NanoChat number; a character without one takes the candidate. Null when the candidate is taken.</summary>
    Task<int?> ClaimNanoChatNumberAsync(int profileId, int candidate, CancellationToken cancel = default);
}

public sealed partial class ServerDbManager
{
    public Task<List<int>> GetNanoChatNumbersAsync(CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetNanoChatNumbersAsync(cancel));
    }

    public Task<int?> ClaimNanoChatNumberAsync(int profileId, int candidate, CancellationToken cancel = default)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.ClaimNanoChatNumberAsync(profileId, candidate, cancel));
    }
}
