using System.Linq;
using System.Numerics;
using Content.Server._Mono.Detection;
using Content.Server._Mono.FireControl;
using Content.Server._WG.ShipShields;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Mono.Detection;
using Content.Shared._Mono.FireControl;
using Content.Shared._WG.ShipShields;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Spawners;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    [Dependency] private SharedPopupSystem _wgShieldPopup = default!;
    [Dependency] private SharedUserInterfaceSystem _wgShieldUi = default!;

    /// <summary>Lowest share of thrust or turning a raised field can leave a ship.</summary>
    private const float MinShieldMobility = 0.1f;

    /// <summary>When each ship's guns unlock after its field was beaten down.</summary>
    private readonly Dictionary<EntityUid, TimeSpan> _wgGunLocks = new();

    private void InitializeWolfgateShieldCosts()
    {
        SubscribeLocalEvent<ShipShieldedComponent, GetThermalSignatureEvent>(OnWolfgateShieldSignature);
        SubscribeLocalEvent<FireControllableComponent, AttemptShootEvent>(OnWolfgateLockedGunShoot);
        InitializeWolfgateShipHeat();
        InitializeWolfgateVents();
    }

    /// <summary>The cost component of the generator holding a ship's raised field, if it has one.</summary>
    private WGShipShieldCostComponent? GetWolfgateRaisedCost(EntityUid grid)
    {
        return TryComp<ShipShieldedComponent>(grid, out var shielded) && shielded.Source is { } source &&
            IsWolfgateShieldLive(shielded.Shield) && TryComp<WGShipShieldCostComponent>(source, out var cost)
            ? cost : null;
    }

    /// <summary>Whether a ship has a live field raised by a ship generator, not an outpost's.</summary>
    public bool IsWolfgateShipFieldRaised(EntityUid grid)
    {
        return GetWolfgateRaisedCost(grid) != null;
    }

    /// <summary>Share of linear thrust a ship keeps under its raised field; 1 with the field down.</summary>
    public float GetWolfgateShieldThrustFactor(EntityUid grid)
    {
        return GetWolfgateRaisedCost(grid) is { } cost ? Math.Clamp(cost.ThrustFactor, MinShieldMobility, 1f) : 1f;
    }

    /// <summary>Share of turning thrust a ship keeps under its raised field; 1 with the field down.</summary>
    public float GetWolfgateShieldTurnFactor(EntityUid grid)
    {
        return GetWolfgateRaisedCost(grid) is { } cost ? Math.Clamp(cost.TurnFactor, MinShieldMobility, 1f) : 1f;
    }

    private void OnWolfgateShieldSignature(Entity<ShipShieldedComponent> grid, ref GetThermalSignatureEvent args)
    {
        if (GetWolfgateRaisedCost(grid) is { } cost)
            args.Signature += MathF.Max(0f, cost.Signature);
    }

    /// <summary>Flares the protected ship's thermal signature for damage its field absorbed.</summary>
    private void AddWolfgateShieldHitHeat(ShipShieldComponent shield, float damage)
    {
        if (shield.Source is { } source && TryComp<WGShipShieldCostComponent>(source, out var cost) && cost.HitSignature > 0f &&
            TryComp<ThermalSignatureComponent>(shield.Shielded, out var heat))
            heat.StoredHeat += damage * cost.HitSignature;
    }

    /// <summary>Locks a ship's guns after damage beat its field down, sets them sparking, and tells whoever is at its gunnery consoles.</summary>
    private void LockWolfgateShieldGuns(EntityUid grid, EntityUid emitter)
    {
        if (!TryComp<WGShipShieldCostComponent>(emitter, out var cost))
            return;
        if (TryComp<WGShipHeatComponent>(grid, out var hull))
        {
            hull.Collapsed = true;
            // Shed below the mark where hot guns resume, a collapse leaves the guns to the lock alone.
            if (cost.CollapseHeat is { } left)
            {
                var most = GetWolfgateShipHeat(grid).Capacity * Math.Clamp(left, 0f, 1f);
                if (hull.Heat > most)
                {
                    hull.Heat = most;
                    hull.Hard = MathF.Min(hull.Hard, most);
                    SyncWolfgateShipHeat(grid);
                }
            }
        }
        // The guns stay locked through the whole overload, as an overloaded ship can't fire in Starsector, unless the
        // generator sets a lock of its own.
        var seconds = cost.GunLockout ??
            (TryComp<ShipShieldEmitterComponent>(emitter, out var shield) ? WolfgateOverloadSeconds(shield) : 0f);
        if (seconds <= 0f)
            return;
        var now = _wgShieldTiming.CurTime;
        foreach (var expired in _wgGunLocks.Where(l => l.Value <= now).Select(l => l.Key).ToArray())
            _wgGunLocks.Remove(expired);
        _wgGunLocks[grid] = now + TimeSpan.FromSeconds(seconds);

        if (cost.GunLockEffect is { } effect)
        {
            var guns = EntityQueryEnumerator<FireControllableComponent, TransformComponent>();
            while (guns.MoveNext(out var gun, out _, out var gunTransform))
            {
                if (gunTransform.GridUid != grid || !gunTransform.Anchored)
                    continue;
                var sparks = Spawn(effect, new EntityCoordinates(gun, Vector2.Zero));
                EnsureComp<TimedDespawnComponent>(sparks).Lifetime = seconds;
            }
        }

        PopupWolfgateShipConsoles(grid,
            Loc.GetString("wg-shield-gun-lock-popup", ("seconds", MathF.Ceiling(seconds))), PopupType.LargeCaution);
    }

    /// <summary>
    /// Whole seconds until a ship's guns unlock after a field collapse; 0 when they are free. They stay locked for as
    /// long as the ship's generator waits out its overload, or for its <c>gunLockout</c> if it sets one.
    /// </summary>
    public int GetWolfgateGunLockSeconds(EntityUid grid)
    {
        var seconds = 0d;
        if (_wgGunLocks.TryGetValue(grid, out var until))
        {
            var left = until - _wgShieldTiming.CurTime;
            if (left > TimeSpan.Zero)
                seconds = left.TotalSeconds;
            else
                _wgGunLocks.Remove(grid);
        }
        if (TryComp<WGShipHeatComponent>(grid, out var hull) && hull.Cooler is { } cooler &&
            TryComp<WGShipShieldCostComponent>(cooler, out var cost) && cost.GunLockout == null &&
            TryComp<ShipShieldEmitterComponent>(cooler, out var emitter) && Transform(cooler).GridUid == grid)
            seconds = Math.Max(seconds, WolfgateOverloadSeconds(emitter));
        return (int) Math.Ceiling(seconds);
    }

    private void OnWolfgateLockedGunShoot(Entity<FireControllableComponent> gun, ref AttemptShootEvent args)
    {
        if (args.Cancelled || Transform(gun).GridUid is not { } grid)
            return;
        if (IsWolfgateShipVenting(grid) || GetWolfgateGunLockSeconds(grid) > 0 || IsWolfgateShipHeatHeld(grid) ||
            HasComp<WGShipShieldHardenedComponent>(grid) ||
            _wgNpcHulls.ContainsKey(grid) && WantsWolfgateNpcHoldFire(grid))
            args.Cancelled = true;
    }
}
