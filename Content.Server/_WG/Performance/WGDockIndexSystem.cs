using Content.Server.Shuttles.Components;

namespace Content.Server._WG.Performance;

/// <summary>
/// Keeps each entity's docking ports indexed by their parent, so finding a ship's docks no longer walks every entity
/// on the ship.
/// </summary>
public sealed class WGDockIndexSystem : EntitySystem
{
    private readonly Dictionary<EntityUid, HashSet<EntityUid>> _docks = new();
    private readonly Dictionary<EntityUid, EntityUid> _parents = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DockingComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DockingComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<DockingComponent, EntParentChangedMessage>(OnParentChanged);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _docks.Clear();
        _parents.Clear();
    }

    /// <summary>Docks whose direct parent is the given entity, or null when it has none.</summary>
    public IReadOnlyCollection<EntityUid>? DocksOn(EntityUid parent)
    {
        return _docks.GetValueOrDefault(parent);
    }

    private void OnInit(Entity<DockingComponent> ent, ref ComponentInit args)
    {
        Track(ent, Transform(ent).ParentUid);
    }

    private void OnRemove(Entity<DockingComponent> ent, ref ComponentRemove args)
    {
        Untrack(ent);
    }

    private void OnParentChanged(Entity<DockingComponent> ent, ref EntParentChangedMessage args)
    {
        Untrack(ent);
        Track(ent, args.Transform.ParentUid);
    }

    private void Track(EntityUid dock, EntityUid parent)
    {
        if (!parent.IsValid())
            return;

        if (!_docks.TryGetValue(parent, out var set))
            _docks[parent] = set = new HashSet<EntityUid>();

        set.Add(dock);
        _parents[dock] = parent;
    }

    private void Untrack(EntityUid dock)
    {
        if (!_parents.Remove(dock, out var parent) || !_docks.TryGetValue(parent, out var set))
            return;

        set.Remove(dock);
        if (set.Count == 0)
            _docks.Remove(parent);
    }
}
