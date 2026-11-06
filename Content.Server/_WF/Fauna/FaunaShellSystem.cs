using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Explosion;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Flips <see cref="FaunaShellComponent"/> mobs when an explosion hits them and rights them again later, and opens
/// retractable shells while the mob attacks.
/// </summary>
public sealed partial class FaunaShellSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaShellComponent, BeforeExplodeEvent>(OnBeforeExplode);
        SubscribeLocalEvent<FaunaShellComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaShellComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<FaunaShellComponent, MeleeHitEvent>(OnMeleeHit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaShellComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Flipped && now >= comp.RightAt)
                SetFlipped((uid, comp), false);

            if (comp.Open && now >= comp.CloseAt)
            {
                comp.Open = false;
                if (!comp.Flipped)
                    _damageable.SetDamageModifierSetId(uid, comp.ShellModifierSet);
            }
        }
    }

    private void OnBeforeExplode(Entity<FaunaShellComponent> ent, ref BeforeExplodeEvent args)
    {
        if (ent.Comp.FlipOnExplode && !ent.Comp.Flipped)
            SetFlipped(ent, true);
    }

    private void OnGunShot(Entity<FaunaShellComponent> ent, ref GunShotEvent args)
    {
        OpenShell(ent);
    }

    private void OnMeleeHit(Entity<FaunaShellComponent> ent, ref MeleeHitEvent args)
    {
        if (args.User == ent.Owner && args.IsHit)
            OpenShell(ent);
    }

    private void OpenShell(Entity<FaunaShellComponent> ent)
    {
        if (ent.Comp.OpenModifierSet is not { } open || ent.Comp.Flipped)
            return;

        ent.Comp.Open = true;
        ent.Comp.CloseAt = _timing.CurTime + ent.Comp.OpenDuration;
        _damageable.SetDamageModifierSetId(ent, open);
    }

    private void OnMobStateChanged(Entity<FaunaShellComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead && ent.Comp.Flipped)
            SetFlipped(ent, false, false);
    }

    private void SetFlipped(Entity<FaunaShellComponent> ent, bool flipped, bool popup = true)
    {
        ent.Comp.Flipped = flipped;
        ent.Comp.Open = false;
        _damageable.SetDamageModifierSetId(ent, flipped ? ent.Comp.FlippedModifierSet : ent.Comp.ShellModifierSet);
        _appearance.SetData(ent, FaunaShellVisuals.Flipped, flipped);

        if (flipped)
        {
            ent.Comp.RightAt = _timing.CurTime + ent.Comp.Duration;
            _stun.TryStun(ent, ent.Comp.Duration, true);
            _popup.PopupEntity(Loc.GetString("fauna-shell-flipped", ("user", ent.Owner)), ent, PopupType.Medium);
            return;
        }

        if (popup)
            _popup.PopupEntity(Loc.GetString("fauna-shell-righted", ("user", ent.Owner)), ent);
    }
}
