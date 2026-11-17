using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Content.Server._WG.ProfileSaves;

/// <summary>
/// Runs each player's character writes one at a time, in the order they were asked for. Writes for different players
/// still overlap freely.
/// </summary>
public sealed class WGUserWriteQueue
{
    private readonly object _lock = new();
    private readonly Dictionary<NetUserId, Task> _tails = new();

    /// <summary>Runs a write once the player's earlier writes have finished, whether or not they succeeded.</summary>
    public async Task Run(NetUserId user, Func<Task> write)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? previous;
        lock (_lock)
        {
            _tails.TryGetValue(user, out previous);
            _tails[user] = done.Task;
        }

        try
        {
            // Tails never fault; a failed write only reaches its own caller.
            if (previous != null)
                await previous;

            await write();
        }
        finally
        {
            lock (_lock)
            {
                if (_tails.TryGetValue(user, out var tail) && tail == done.Task)
                    _tails.Remove(user);
            }

            done.SetResult();
        }
    }
}
