using System.Reflection;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WF.StateRecovery;

/// <summary>
/// Keeps one entry per chunk in each grid's chunk deletion history, so the engine's grid state doesn't throw on a
/// chunk deleted twice before the oldest ack.
/// </summary>
/// <remarks>
/// The throw fails every player's game state for that tick, after which the engine wipes all deletion history and
/// clients keep dead chunks. Remove this once RobustToolbox's <c>OnGridGetState</c> tolerates duplicates.
/// </remarks>
public sealed partial class WFGridDeletionHistorySystem : EntitySystem
{
    private static readonly FieldInfo? HistoryField =
        typeof(MapGridComponent).GetField("ChunkDeletionHistory", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HashSet<Vector2i> _seen = new();
    private bool _valid;

    public override void Initialize()
    {
        base.Initialize();

        _valid = HistoryField?.FieldType == typeof(List<(GameTick, Vector2i)>);
        if (!_valid)
            Log.Error("MapGridComponent.ChunkDeletionHistory changed shape; duplicate chunk deletions are no longer removed.");
    }

    /// <summary>
    /// Drops older duplicate entries from every grid's chunk deletion history. Runs after all systems update and
    /// before game states are built.
    /// </summary>
    public void Dedupe()
    {
        if (!_valid)
            return;

        var query = EntityQueryEnumerator<MapGridComponent>();
        while (query.MoveNext(out var grid))
        {
            if (HistoryField!.GetValue(grid) is not List<(GameTick Tick, Vector2i Indices)> { Count: > 1 } history)
                continue;

            _seen.Clear();
            var duplicate = false;
            foreach (var (_, indices) in history)
            {
                if (!_seen.Add(indices))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                continue;

            // Newest entries are at the end; keep the newest per chunk, in order.
            _seen.Clear();
            for (var i = history.Count - 1; i >= 0; i--)
            {
                if (!_seen.Add(history[i].Indices))
                    history.RemoveAt(i);
            }
        }
    }
}
