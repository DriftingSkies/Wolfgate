using Content.Shared.Damage;
using Content.Shared._WG.Xenonids;
using Content.Shared.Doors;
using Content.Shared.FixedPoint;

namespace Content.Shared._WG.Barricades;

/// <summary>Unanchored barricades take extra damage; folding barricade doors only close while anchored and xenos can't open them.</summary>
public sealed class BarricadeAnchorSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<DamageReceivedModifierWhenUnanchoredComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<RMCOpenOnlyWhenUnanchoredComponent, BeforeDoorClosedEvent>(OnBeforeDoorClosed);
        SubscribeLocalEvent<RMCOpenOnlyWhenUnanchoredComponent, BeforeDoorOpenedEvent>(OnBeforeDoorOpened);
    }

    private void OnDamageModify(Entity<DamageReceivedModifierWhenUnanchoredComponent> ent, ref DamageModifyEvent args)
    {
        if (Transform(ent).Anchored || args.Damage.GetTotal() <= FixedPoint2.Zero)
            return;

        args.Damage *= ent.Comp.Multiplier;
    }

    private void OnBeforeDoorClosed(Entity<RMCOpenOnlyWhenUnanchoredComponent> ent, ref BeforeDoorClosedEvent args)
    {
        if (!Transform(ent).Anchored)
            args.Cancel();
    }

    private void OnBeforeDoorOpened(Entity<RMCOpenOnlyWhenUnanchoredComponent> ent, ref BeforeDoorOpenedEvent args)
    {
        if (HasComp<XenoComponent>(args.User))
            args.Cancel();
    }
}
