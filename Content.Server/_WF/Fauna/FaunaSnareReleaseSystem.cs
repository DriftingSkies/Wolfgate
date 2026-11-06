using Content.Shared.Destructible;
using Content.Shared.Ensnaring;
using Content.Shared.Ensnaring.Components;
using Robust.Shared.Spawners;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Frees the victim before a snare despawns or is destroyed, while it can still leave the victim's container.
/// </summary>
public sealed partial class FaunaSnareReleaseSystem : EntitySystem
{
    [Dependency] private SharedEnsnareableSystem _ensnareable = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EnsnaringComponent, TimedDespawnEvent>(OnTimedDespawn);
        SubscribeLocalEvent<EnsnaringComponent, DestructionEventArgs>(OnDestruction);
    }

    private void OnTimedDespawn(Entity<EnsnaringComponent> ent, ref TimedDespawnEvent args)
    {
        _ensnareable.ForceFree(ent, ent.Comp);
    }

    private void OnDestruction(EntityUid uid, EnsnaringComponent comp, DestructionEventArgs args)
    {
        _ensnareable.ForceFree(uid, comp);
    }
}
