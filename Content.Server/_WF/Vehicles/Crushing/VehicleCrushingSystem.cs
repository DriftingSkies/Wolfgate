using System.Linq;
using Content.Server.Destructible;
using Content.Server.Destructible.Thresholds;
using Content.Server.Destructible.Thresholds.Behaviors;
using Content.Server.Destructible.Thresholds.Triggers;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Crushing;
using Content.Shared._WF.Vehicles.Grid.Components;
using Content.Shared.Damage;
using Content.Shared.GameTicking;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._WF.Vehicles.Crushing;

/// <summary>The server half of crushing: sizes a smash to what the target's destruction needs, and tells the driver what stopped the vehicle.</summary>
public sealed partial class VehicleCrushingSystem : EntitySystem
{
    private static readonly TimeSpan PopupInterval = TimeSpan.FromSeconds(2);

    /// <summary>A hit smaller than this would not be a hit, whatever the target has left to take.</summary>
    private const double MinimumHit = 1;

    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<EntityUid, TimeSpan> _nextPopup = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DestructibleComponent, VehicleSmashDamageEvent>(OnSmashDamage);
        SubscribeLocalEvent<GridVehicleMoverComponent, VehicleSmashBlockedEvent>(OnSmashBlocked);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _nextPopup.Clear();
    }

    /// <summary>Lowers the hit to what the target still needs to reach its lowest destruction threshold.</summary>
    private void OnSmashDamage(Entity<DestructibleComponent> ent, ref VehicleSmashDamageEvent args)
    {
        if (!TryComp(ent, out DamageableComponent? damageable) || !TryGetDestructionDamage(ent.Comp, out var needed))
            return;

        var missing = (needed - damageable.TotalDamage).Double();
        args.Damage = Math.Min(args.Damage, Math.Max(missing, MinimumHit));
    }

    /// <summary>The lowest total damage at which a threshold of the component destroys the entity.</summary>
    public static bool TryGetDestructionDamage(DestructibleComponent destructible, out int damage)
    {
        damage = int.MaxValue;

        foreach (var threshold in destructible.Thresholds)
        {
            if (threshold.Trigger is not DamageTrigger trigger)
                continue;

            foreach (var behavior in threshold.Behaviors)
            {
                if (behavior is DoActsBehavior acts && acts.HasAct(ThresholdActs.Destruction))
                    damage = Math.Min(damage, trigger.Damage);
            }
        }

        return damage != int.MaxValue;
    }

    private void OnSmashBlocked(Entity<GridVehicleMoverComponent> ent, ref VehicleSmashBlockedEvent args)
    {
        if (!TryComp(ent, out RMCVehicleComponent? vehicle) || vehicle.Operator is not { } driver)
            return;

        var now = _timing.CurTime;
        if (_nextPopup.TryGetValue(ent, out var next) && now < next)
            return;

        // Vehicles come and go, and this has no hook for their deletion; a clean-out now and then keeps it small.
        if (_nextPopup.Count > 32)
        {
            foreach (var known in _nextPopup.Keys.ToArray())
            {
                if (TerminatingOrDeleted(known))
                    _nextPopup.Remove(known);
            }
        }

        _nextPopup[ent] = now + PopupInterval;

        var key = args.Reason == VehicleSmashBlockReason.DoorPowered
            ? "wf-vehicle-crush-door-powered"
            : "wf-vehicle-crush-too-light";

        _popup.PopupEntity(Loc.GetString(key, ("vehicle", ent.Owner), ("target", args.Target)), ent, driver, PopupType.Small);
    }
}
