using Content.Server._WG.Wolfmed.Life;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WG.Wolfmed.EntityEffects;
using Content.Shared.FixedPoint;

namespace Content.Server._WG.Wolfmed.Medical;

/// <summary>Mannitol's effect: a damaged brain that still works gets its health back.</summary>
public sealed partial class WolfmedBrainMendSystem : EntitySystem
{
    [Dependency] private OrganHealthSystem _organs = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundHostComponent, WolfmedMendBrainEvent>(OnMend);
    }

    private void OnMend(Entity<WoundHostComponent> body, ref WolfmedMendBrainEvent args)
    {
        // A brain at zero is dead; that is surgery's, not a pill's.
        if (_life.GetBrainOrgan(body) is not { } brain || brain.Comp.Health <= FixedPoint2.Zero ||
            brain.Comp.Health >= brain.Comp.MaxHealth)
            return;

        _organs.ChangeHealth(brain, args.Amount);
    }
}
