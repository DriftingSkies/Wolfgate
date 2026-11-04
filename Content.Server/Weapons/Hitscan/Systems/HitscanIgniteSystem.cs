// WOLFGATE(Xenoborgs) START: wizden#43014 hitscan ignition, ported and adapted to Mono's multi-hit hitscan event.
using Content.Shared.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Robust.Shared.Random;

namespace Content.Server.Weapons.Hitscan.Systems;

public sealed partial class HitscanIgniteSystem : EntitySystem
{
    [Dependency] private IRobustRandom _robustRandom = default!;
    [Dependency] private FlammableSystem _flammable = default!;

    //The hitscan has hit the targets, rolls a chance to ignite each and ignites it if it succeeds.
    [SubscribeLocalEvent]
    private void OnHitscanHit(Entity<HitscanIgniteComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        foreach (var target in args.HitEntities)
        {
            //Rolls a chance for the laser to ignite the target. Skips it if it fails.
            if (!_robustRandom.Prob(ent.Comp.IgniteChance))
                continue;

            if (TryComp<FlammableComponent>(target, out var flamcmp))
                _flammable.AdjustFireStacks(target, ent.Comp.FireStacks, flamcmp, true);
        }
    }
}
// WOLFGATE END
