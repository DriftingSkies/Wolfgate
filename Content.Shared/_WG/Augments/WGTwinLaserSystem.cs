using System.Linq;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared._WG.Augments;

/// <summary>
/// Fires both arm lasers together when one is in each hand, and widens their spread while they are. Shared, so the
/// shooter's client predicts the second shot as well.
/// </summary>
public sealed class WGTwinLaserSystem : EntitySystem
{
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>Set while the twin's shot is fired, so that shot doesn't fire the first one again.</summary>
    private bool _twinning;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGTwinLaserComponent, GotEquippedHandEvent>(OnEquipped);
        SubscribeLocalEvent<WGTwinLaserComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<WGTwinLaserComponent, GunRefreshModifiersEvent>(OnRefresh);
        SubscribeLocalEvent<WGTwinLaserComponent, GunShotEvent>(OnShot);
    }

    // The gun system refreshes the laser that moved; the one already in the other hand is refreshed here.
    private void OnEquipped(Entity<WGTwinLaserComponent> ent, ref GotEquippedHandEvent args)
    {
        RefreshTwins(args.User, ent);
    }

    private void OnUnequipped(Entity<WGTwinLaserComponent> ent, ref GotUnequippedHandEvent args)
    {
        RefreshTwins(args.User, ent);
    }

    private void RefreshTwins(EntityUid user, EntityUid laser)
    {
        foreach (var twin in Twins(user, laser))
        {
            _gun.RefreshModifiers(twin);
        }
    }

    private void OnRefresh(Entity<WGTwinLaserComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (args.Gun.Comp.Holder is not { } user || !Twins(user, ent).Any())
            return;

        args.MinAngle *= ent.Comp.Spread;
        args.MaxAngle *= ent.Comp.Spread;
        args.AngleIncrease *= ent.Comp.Spread;
    }

    private void OnShot(Entity<WGTwinLaserComponent> ent, ref GunShotEvent args)
    {
        if (_twinning)
            return;

        _twinning = true;
        try
        {
            foreach (var twin in Twins(args.User, ent))
            {
                if (TryComp<GunComponent>(twin, out var gun))
                    _gun.AttemptShoot(args.User, twin, gun, args.ToCoordinates);
            }
        }
        finally
        {
            _twinning = false;
        }
    }

    /// <summary>The other arm lasers the user holds.</summary>
    private IEnumerable<EntityUid> Twins(EntityUid user, EntityUid laser)
    {
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (held != laser && HasComp<WGTwinLaserComponent>(held))
                yield return held;
        }
    }
}
