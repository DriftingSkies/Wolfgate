using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.ShipModules;

/// <summary>
/// Accelerated ammo feeder: for the module's duration every ballistic ship gun on the hull fires
/// <see cref="FireRateFactor"/> times as fast, bursts and the pause between them included. Energy guns and missile
/// launchers are left alone.
/// </summary>
public sealed partial class WGAmmoFeederEffect : WGShipModuleEffect
{
    /// <summary>Multiplier on each ballistic gun's rate of fire.</summary>
    [DataField]
    public float FireRateFactor = 2f;
}

/// <summary>On a ballistic ship gun while an ammo feeder drives it; networked so clients tint it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, UnsavedComponent]
public sealed partial class WGAmmoFeederBoostedComponent : Component
{
    /// <summary>Multiplier on the gun's rate of fire.</summary>
    [DataField, AutoNetworkedField]
    public float FireRateFactor = 2f;
}

/// <summary>Speeds up the guns an ammo feeder drives: the time between shots, and the pause after a burst.</summary>
public sealed partial class WGAmmoFeederRateSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGAmmoFeederBoostedComponent, QueryFireRateMultiplierEvent>(OnQueryRate);
        SubscribeLocalEvent<WGAmmoFeederBoostedComponent, GunShotEvent>(OnShot);
    }

    /// <summary>Shortens the time to the next shot, in or out of a burst.</summary>
    private void OnQueryRate(Entity<WGAmmoFeederBoostedComponent> ent, ref QueryFireRateMultiplierEvent args)
    {
        args.ReloadTimeMul /= Factor(ent.Comp);
    }

    /// <summary>Takes back the share of a burst's cooldown the feeder saves, once the burst has ended.</summary>
    private void OnShot(Entity<WGAmmoFeederBoostedComponent> ent, ref GunShotEvent args)
    {
        if (!TryComp<GunComponent>(ent, out var gun) || gun.SelectedMode != SelectiveFire.Burst || gun.BurstActivated)
            return;

        gun.NextFire -= TimeSpan.FromSeconds(gun.BurstCooldown * (1f - 1f / Factor(ent.Comp)));
        DirtyField(ent.Owner, gun, nameof(GunComponent.NextFire));
    }

    private static float Factor(WGAmmoFeederBoostedComponent comp)
    {
        return MathF.Max(comp.FireRateFactor, 0.01f);
    }
}
