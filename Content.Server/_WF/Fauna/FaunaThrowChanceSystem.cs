using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Cancels melee throws of <see cref="FaunaThrowChanceComponent"/> mobs that fail their roll.
/// </summary>
public sealed partial class FaunaThrowChanceSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaThrowChanceComponent, AttemptMeleeThrowOnHitEvent>(OnAttemptThrow);
    }

    private void OnAttemptThrow(Entity<FaunaThrowChanceComponent> ent, ref AttemptMeleeThrowOnHitEvent args)
    {
        if (ent.Comp.MaxMass > 0f
            && TryComp<PhysicsComponent>(args.Target, out var body)
            && body.Mass > ent.Comp.MaxMass)
        {
            args.Cancelled = true;
            if (ent.Comp.ResistPopup is { } popup)
                _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner), ("target", args.Target)), args.Target);

            return;
        }

        if (!_random.Prob(ent.Comp.Chance))
            args.Cancelled = true;
    }
}
