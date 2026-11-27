using System.Linq;
using Content.Server._Mono.FireControl;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Runs the accelerated ammo feeder: for its duration every ballistic ship gun on the hull carries
/// <see cref="WGAmmoFeederBoostedComponent"/>, which raises its rate of fire. Energy guns and missile launchers are
/// left alone.
/// </summary>
public sealed partial class WGAmmoFeederSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    /// <summary>The container a magazine-fed gun holds its magazine in.</summary>
    private const string MagazineSlot = "gun_magazine";

    /// <summary>Whether each cartridge or projectile prototype is a missile, once looked up.</summary>
    private readonly Dictionary<string, bool> _missiles = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<WGShipModuleDeactivatedEvent>(OnDeactivated);
    }

    private void OnActivated(ref WGShipModuleActivatedEvent args)
    {
        if (args.Module.Effect is not WGAmmoFeederEffect feeder)
            return;

        var active = EnsureComp<WGAmmoFeederActiveComponent>(args.Grid);
        var guns = EntityQueryEnumerator<FireControllableComponent, GunComponent, TransformComponent>();
        while (guns.MoveNext(out var gun, out _, out _, out var xform))
        {
            if (xform.GridUid != args.Grid || !IsBallistic(gun))
                continue;

            var boosted = EnsureComp<WGAmmoFeederBoostedComponent>(gun);
            boosted.FireRateFactor = feeder.FireRateFactor;
            Dirty(gun, boosted);
            active.Guns.Add(gun);
        }
    }

    private void OnDeactivated(ref WGShipModuleDeactivatedEvent args)
    {
        if (args.Module.Effect is not WGAmmoFeederEffect || !TryComp<WGAmmoFeederActiveComponent>(args.Grid, out var active))
            return;

        foreach (var gun in active.Guns)
        {
            if (!TerminatingOrDeleted(gun))
                RemComp<WGAmmoFeederBoostedComponent>(gun);
        }

        RemComp<WGAmmoFeederActiveComponent>(args.Grid);
    }

    /// <summary>
    /// Whether a ship gun is ballistic: its Mono gun type says so, even for a cannon a battery powers; an untyped gun
    /// counts when it is fed rounds that aren't missiles.
    /// </summary>
    public bool IsBallistic(EntityUid gun)
    {
        if (TryComp<ShipGunTypeComponent>(gun, out var type))
            return type.Type == ShipGunType.Ballistic;

        if (HasComp<HitscanBatteryAmmoProviderComponent>(gun) || HasComp<ProjectileBatteryAmmoProviderComponent>(gun))
            return false;

        if (TryComp<BallisticAmmoProviderComponent>(gun, out var ballistic))
            return !FeedsMissiles(ballistic);

        if (TryComp<BasicEntityAmmoProviderComponent>(gun, out var basic))
            return !IsMissile(basic.Proto);

        if (HasComp<MagazineAmmoProviderComponent>(gun))
        {
            return !(_containers.TryGetContainer(gun, MagazineSlot, out var slot)
                     && slot.ContainedEntities.FirstOrDefault() is { Valid: true } magazine
                     && TryComp<BallisticAmmoProviderComponent>(magazine, out var fed)
                     && FeedsMissiles(fed));
        }

        return false;
    }

    private bool FeedsMissiles(BallisticAmmoProviderComponent provider)
    {
        if (provider.Proto is { } proto && IsMissile(proto))
            return true;

        foreach (var round in provider.Entities)
        {
            if (MetaData(round).EntityPrototype is { } prototype && IsMissile(prototype.ID))
                return true;
        }

        return provider.Container != null && provider.Container.ContainedEntities.Any(round =>
            MetaData(round).EntityPrototype is { } prototype && IsMissile(prototype.ID));
    }

    /// <summary>Whether a cartridge fires, or a projectile is, a missile.</summary>
    private bool IsMissile(string id)
    {
        if (_missiles.TryGetValue(id, out var cached))
            return cached;

        var missile = false;
        if (_prototype.TryIndex<EntityPrototype>(id, out var prototype))
        {
            missile = prototype.TryComp<WGShipMissileComponent>(out _, _factory)
                      || prototype.TryComp<CartridgeAmmoComponent>(out var cartridge, _factory)
                      && _prototype.TryIndex(cartridge.Prototype, out var shot)
                      && shot.TryComp<WGShipMissileComponent>(out _, _factory);
        }

        _missiles[id] = missile;
        return missile;
    }
}

/// <summary>An ammo feeder running on a ship's grid, with the guns it drives.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WGAmmoFeederActiveComponent : Component
{
    [ViewVariables]
    public List<EntityUid> Guns = new();
}
