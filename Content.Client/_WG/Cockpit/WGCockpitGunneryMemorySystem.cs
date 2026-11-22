using Content.Shared.GameTicking;

namespace Content.Client._WG.Cockpit;

/// <summary>
/// Remembers which weapons the pilot had selected on each gunnery console, so the cockpit selects them again when the
/// helm closes and reopens or the gunnery link drops and comes back. Forgotten when the round ends.
/// </summary>
public sealed class WGCockpitGunneryMemorySystem : EntitySystem
{
    private static readonly IReadOnlyCollection<NetEntity> Nothing = Array.Empty<NetEntity>();

    private readonly Dictionary<NetEntity, HashSet<NetEntity>> _selected = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => _selected.Clear());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _selected.Clear();
    }

    /// <summary>Notes the weapons selected on a console; none forgets the console.</summary>
    public void Remember(NetEntity console, IEnumerable<NetEntity> weapons)
    {
        var selected = new HashSet<NetEntity>(weapons);
        if (selected.Count == 0)
            _selected.Remove(console);
        else
            _selected[console] = selected;
    }

    /// <summary>The weapons last selected on a console.</summary>
    public IReadOnlyCollection<NetEntity> Recall(NetEntity console)
    {
        return _selected.TryGetValue(console, out var selected) ? selected : Nothing;
    }
}
