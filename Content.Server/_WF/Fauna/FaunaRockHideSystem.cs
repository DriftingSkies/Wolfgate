using Content.Server.Popups;
using Content.Shared.Damage;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Cuts weak projectile and thrown damage to <see cref="FaunaRockHideComponent"/> mobs.
/// </summary>
public sealed partial class FaunaRockHideSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaRockHideComponent, DamageModifyEvent>(OnDamageModify);
    }

    private void OnDamageModify(Entity<FaunaRockHideComponent> ent, ref DamageModifyEvent args)
    {
        if (args.Tool is not { } tool || _whitelist.IsWhitelistPass(ent.Comp.Exempt, tool))
            return;

        var total = args.OriginalDamage.GetTotal().Float();
        if (HasComp<ProjectileComponent>(tool))
        {
            if (total < ent.Comp.ProjectileThreshold)
                args.Damage *= ent.Comp.ProjectileMultiplier;

            return;
        }

        if (!HasComp<ThrownItemComponent>(tool) || total > ent.Comp.ThrowThreshold)
            return;

        args.Damage *= 0f;
        _popup.PopupEntity(Loc.GetString(ent.Comp.BouncePopup, ("item", tool), ("target", ent.Owner)), ent);
    }
}
