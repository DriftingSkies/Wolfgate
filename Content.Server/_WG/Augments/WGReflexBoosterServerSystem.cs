using Content.Server.Physics.Controllers;
using Content.Shared._WG.Augments;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Robust.Shared.Random;

namespace Content.Server._WG.Augments;

/// <summary>Lets a body with a reflex booster fly ships better and slip the odd bullet.</summary>
public sealed partial class WGReflexBoosterServerSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGReflexBoosterComponent, GetShuttleInputsEvent>(OnGetShuttleInputs);
        SubscribeLocalEvent<WGReflexBoosterComponent, ProjectileReflectAttemptEvent>(OnProjectile);
    }

    private void OnGetShuttleInputs(Entity<WGReflexBoosterComponent> ent, ref GetShuttleInputsEvent args)
    {
        args.AccelMul *= ent.Comp.ShipHandling;
        args.AngularMul *= ent.Comp.ShipHandling;
    }

    /// <summary>A bullet is about to hit. If the body slips it, the bullet flies on through.</summary>
    private void OnProjectile(Entity<WGReflexBoosterComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        // A body in crit or dead can't move out of the way.
        if (args.Cancelled || !_mobState.IsAlive(ent) || !_random.Prob(ent.Comp.DodgeChance))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wg-reflex-booster-dodge", ("target", ent.Owner)), ent);
    }
}
