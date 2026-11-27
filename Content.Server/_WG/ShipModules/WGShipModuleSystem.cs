using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Shuttles;
using Content.Server.Shuttles.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipModules;
using Content.Shared._WG.ShipShields;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipModules;

/// <summary>
/// Holds the modules fitted into ships' slots: refills their charges while the ship has a powered helm, publishes each
/// ship's strip to its helm and fires them for its pilots.
/// </summary>
public sealed partial class WGShipModuleSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Grids with a powered helm at the last sweep.</summary>
    private readonly HashSet<EntityUid> _poweredHelms = new();

    /// <summary>Grids with a shield generator switched on at the last sweep.</summary>
    private readonly HashSet<EntityUid> _shieldsOn = new();

    private readonly List<WGShipModuleSlot> _slots = new();
    private readonly List<WGFittedModule> _fitted = new();
    private readonly List<WGFittedModule> _stripFitted = new();
    private TimeSpan _nextSweep;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGShipModuleActivateEvent>(OnActivateRequest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGShipModuleBayComponent>();
        while (query.MoveNext(out var grid, out var bay))
        {
            var powered = _poweredHelms.Contains(grid);
            // Effect handlers may switch modules off, so walk a copy.
            _fitted.Clear();
            _fitted.AddRange(bay.Fitted);
            foreach (var fitted in _fitted)
            {
                if (!_prototype.TryIndex(fitted.Module, out var module))
                    continue;

                // A passive module starts as soon as it is fitted or its ship loads.
                if (module.Mode == WGShipModuleMode.Passive)
                {
                    if (!fitted.Active)
                        StartEffect(grid, fitted, module, null);
                    continue;
                }

                if (fitted.Active && module.Mode == WGShipModuleMode.Fired && now >= fitted.ActiveUntil)
                    StopEffect(grid, fitted, module, true);

                if (module.Mode == WGShipModuleMode.Toggle)
                    continue;

                if (fitted.Charges < 0)
                    fitted.Charges = module.MaxCharges;

                if (fitted.Charges >= module.MaxCharges)
                    continue;

                // Without a powered helm a module keeps the progress it had.
                if (!powered)
                {
                    fitted.NextCharge += TimeSpan.FromSeconds(frameTime);
                    continue;
                }

                if (now < fitted.NextCharge)
                    continue;

                fitted.Charges++;
                fitted.NextCharge = now + module.RechargeTime;
            }
        }

        if (now < _nextSweep)
            return;

        _nextSweep = now + SweepInterval;
        Sweep(now);
    }

    /// <summary>Notes which ships have a powered helm or a shield generator on, then rebuilds every ship's module strip.</summary>
    private void Sweep(TimeSpan now)
    {
        _poweredHelms.Clear();
        _shieldsOn.Clear();
        // With no module fitted anywhere there is nothing to keep current.
        if (EntityManager.Count<WGShipModuleBayComponent>() > 0)
        {
            var helms = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            while (helms.MoveNext(out var uid, out _, out var xform))
            {
                if (xform.GridUid is { } grid && _power.IsPowered(uid))
                    _poweredHelms.Add(grid);
            }

            var emitters = EntityQueryEnumerator<ShipShieldEmitterComponent, ApcPowerReceiverComponent, TransformComponent>();
            while (emitters.MoveNext(out var uid, out _, out var power, out var xform))
            {
                if (xform.GridUid is { } grid && GeneratorOn(uid, power, xform, grid))
                    _shieldsOn.Add(grid);
            }
        }

        var bays = EntityQueryEnumerator<WGShipModuleBayComponent>();
        while (bays.MoveNext(out var grid, out var bay))
        {
            UpdateStrip(grid, bay, now);
        }

        var strips = EntityQueryEnumerator<WGShipModulesComponent>();
        while (strips.MoveNext(out var grid, out _))
        {
            if (!HasComp<WGShipModuleBayComponent>(grid))
                RemCompDeferred<WGShipModulesComponent>(grid);
        }
    }

    /// <summary>
    /// Whether a shield generator on a ship is switched on, at the machine and at the ship's field switch, with or
    /// without power or a field up. A deflector never counts.
    /// </summary>
    private bool GeneratorOn(EntityUid uid, ApcPowerReceiverComponent power, TransformComponent xform, EntityUid grid)
    {
        return xform.Anchored
               && !power.PowerDisabled
               && !HasComp<WGShipShieldDeflectorComponent>(uid)
               && (!TryComp<WGShipShieldShuntComponent>(grid, out var shunt) || shunt.Enabled);
    }

    /// <summary>
    /// Reads one ship's shield generators now rather than at the last sweep, so one just switched on locks its
    /// modules at once.
    /// </summary>
    private void RefreshShields(EntityUid grid, WGShipModuleBayComponent bay, TimeSpan now)
    {
        _shieldsOn.Remove(grid);
        var emitters = EntityQueryEnumerator<ShipShieldEmitterComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (emitters.MoveNext(out var uid, out _, out var power, out var xform))
        {
            if (xform.GridUid != grid || !GeneratorOn(uid, power, xform, grid))
                continue;

            _shieldsOn.Add(grid);
            break;
        }

        if (ShieldOn(grid))
            bay.LastShielded = now;
    }

    /// <summary>Writes a ship's fitted modules to its helm strip, dirtying it only when something shown changed.</summary>
    private void UpdateStrip(EntityUid grid, WGShipModuleBayComponent bay, TimeSpan now)
    {
        if (TerminatingOrDeleted(grid))
            return;

        if (ShieldOn(grid))
            bay.LastShielded = now;

        _slots.Clear();
        _stripFitted.Clear();
        _stripFitted.AddRange(bay.Fitted);
        foreach (var fitted in _stripFitted)
        {
            if (!_prototype.TryIndex(fitted.Module, out var module))
                continue;

            var charges = fitted.Charges < 0 ? module.MaxCharges : fitted.Charges;
            var locked = module.Mode == WGShipModuleMode.Passive
                ? WGShipModuleLock.None
                : GetLock(grid, bay, module, _poweredHelms.Contains(grid), now);
            // Whatever stops a switched-on or running module switches it off; a dead helm alone doesn't.
            if (fitted.Active && module.Mode != WGShipModuleMode.Passive && locked is not (WGShipModuleLock.None or WGShipModuleLock.Unpowered))
                StopEffect(grid, fitted, module, true);

            var charging = module.Mode == WGShipModuleMode.Fired && charges < module.MaxCharges && locked != WGShipModuleLock.Unpowered;
            var gauge = new WGShipModuleGaugeEvent(grid, module);
            RaiseLocalEvent(ref gauge);
            _slots.Add(new WGShipModuleSlot(fitted.Slot, fitted.Module, charges, module.MaxCharges,
                charging ? fitted.NextCharge : TimeSpan.Zero, fitted.ReadyAt, locked, module.Mode, fitted.Active,
                fitted.Active ? fitted.ActiveUntil : TimeSpan.Zero, gauge.Gauge is { } level ? MathF.Round(level, 2) : null));
        }

        _slots.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
        var strip = EnsureComp<WGShipModulesComponent>(grid);
        if (strip.Slots.SequenceEqual(_slots))
            return;

        strip.Slots = new List<WGShipModuleSlot>(_slots);
        Dirty(grid, strip);
    }

    /// <summary>The module in one of a ship's slots, if any.</summary>
    public WGFittedModule? GetFitted(EntityUid grid, int slot)
    {
        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay))
            return null;

        foreach (var fitted in bay.Fitted)
        {
            if (fitted.Slot == slot)
                return fitted;
        }

        return null;
    }

    /// <summary>
    /// Fits a module into a slot, replacing whatever was there; it starts with full charges. False when the slot is out
    /// of range or the module already sits in another slot, since a ship carries each module once.
    /// </summary>
    public bool Fit(EntityUid grid, int slot, ProtoId<WGShipModulePrototype> module)
    {
        if (slot < 0 || slot >= WGShipModulesComponent.SlotCount || FittedSlot(grid, module) is { } other && other != slot)
            return false;

        var bay = EnsureComp<WGShipModuleBayComponent>(grid);
        if (GetFitted(grid, slot) is { } replaced)
            StopIfActive(grid, replaced);
        bay.Fitted.RemoveAll(f => f.Slot == slot);
        bay.Fitted.Add(new WGFittedModule { Slot = slot, Module = module });
        bay.Fitted.Sort(static (a, b) => a.Slot.CompareTo(b.Slot));
        UpdateStrip(grid, bay, _timing.CurTime);
        return true;
    }

    /// <summary>The slot a module is fitted in on a ship, or null.</summary>
    public int? FittedSlot(EntityUid grid, ProtoId<WGShipModulePrototype> module)
    {
        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay))
            return null;

        foreach (var fitted in bay.Fitted)
        {
            if (fitted.Module == module)
                return fitted.Slot;
        }

        return null;
    }

    /// <summary>Empties a slot; a ship left with no modules loses its strip.</summary>
    public void Unfit(EntityUid grid, int slot)
    {
        if (GetFitted(grid, slot) is { } removed)
            StopIfActive(grid, removed);

        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay) || bay.Fitted.RemoveAll(f => f.Slot == slot) == 0)
            return;

        if (bay.Fitted.Count > 0)
        {
            UpdateStrip(grid, bay, _timing.CurTime);
            return;
        }

        RemComp<WGShipModuleBayComponent>(grid);
        RemComp<WGShipModulesComponent>(grid);
    }

    /// <summary>Why a fitted module can't be used on its ship right now, whatever its charges.</summary>
    public WGShipModuleLock GetLock(EntityUid grid, WGShipModuleBayComponent bay, WGShipModulePrototype module, bool powered,
        TimeSpan now)
    {
        if (!powered)
            return WGShipModuleLock.Unpowered;

        if (TryComp<FTLComponent>(grid, out var ftl) && ftl.State is FTLState.Travelling or FTLState.Arriving)
            return WGShipModuleLock.Ftl;

        if (module.BlockedByShields)
        {
            if (ShieldOn(grid) || bay.LastShielded is { } seen && now < seen + module.ShieldLockout)
                return WGShipModuleLock.Shields;
            // Lowering the field by hand is no way out of a vent or an overload.
            if (HasComp<WGShipVentingComponent>(grid))
                return WGShipModuleLock.Venting;
            if (HasComp<WGShipOverloadedComponent>(grid))
                return WGShipModuleLock.Overloaded;
        }

        var ev = new WGShipModuleLockEvent(grid, module);
        RaiseLocalEvent(ref ev);
        return ev.Lock;
    }

    /// <summary>
    /// Whether the ship had a shield generator switched on at the last sweep, even one recharging with no field up, or
    /// has a field up from any source. A deflector, with its partial field, doesn't count.
    /// </summary>
    public bool ShieldOn(EntityUid grid)
    {
        return _shieldsOn.Contains(grid)
               || TryComp<ShipShieldedComponent>(grid, out var field)
               && !(field.Source is { } source && HasComp<WGShipShieldDeflectorComponent>(source));
    }

    /// <summary>Fires a slot of the ship the sender is piloting; a drone console flies the drone it proxies.</summary>
    private void OnActivateRequest(WGShipModuleActivateEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user
            || !TryComp<PilotComponent>(user, out var pilot)
            || pilot.Console is not { } console)
            return;

        var flown = TryComp<DroneConsoleComponent>(console, out var drone) && drone.Entity is { } remote ? remote : console;
        if (Transform(flown).GridUid is { } grid)
            TryActivate(grid, msg.Slot, user, console);
    }

    /// <summary>
    /// Spends one charge of the module in a ship's slot and runs its effect; tells the user why not otherwise. The helm
    /// it is fired from has to be powered.
    /// </summary>
    public bool TryActivate(EntityUid grid, int slot, EntityUid? user, EntityUid helm)
    {
        return Activate(grid, slot, user, _power.IsPowered(helm));
    }

    /// <summary>Fires a slot for a crew with no helm in hand, such as an NPC; the ship's power is its last sweep's.</summary>
    public bool TryActivateAboard(EntityUid grid, int slot, EntityUid? user)
    {
        return Activate(grid, slot, user, _poweredHelms.Contains(grid));
    }

    private bool Activate(EntityUid grid, int slot, EntityUid? user, bool powered)
    {
        if (!TryComp<WGShipModuleBayComponent>(grid, out var bay)
            || GetFitted(grid, slot) is not { } fitted
            || !_prototype.TryIndex(fitted.Module, out var module))
            return false;

        var now = _timing.CurTime;
        // A held key repeats; the cooldown answers it without a popup.
        if (now < fitted.ReadyAt)
            return false;

        if (module.Mode == WGShipModuleMode.Passive)
        {
            Refuse(user, "wg-ship-module-refused-passive");
            return false;
        }

        // Switching off is always allowed, power or not.
        if (module.Mode == WGShipModuleMode.Toggle && fitted.Active)
        {
            fitted.ReadyAt = now + module.Cooldown;
            StopEffect(grid, fitted, module, true, user, slot);
            UpdateStrip(grid, bay, now);
            return true;
        }

        if (fitted.Active)
        {
            Refuse(user, "wg-ship-module-refused-running");
            return false;
        }

        if (module.BlockedByShields)
            RefreshShields(grid, bay, now);

        var locked = GetLock(grid, bay, module, powered, now);
        if (locked != WGShipModuleLock.None)
        {
            Refuse(user, $"wg-ship-module-refused-{locked.ToString().ToLowerInvariant()}");
            return false;
        }

        var fired = module.Mode == WGShipModuleMode.Fired;
        if (fitted.Charges < 0)
            fitted.Charges = module.MaxCharges;

        if (fired && fitted.Charges <= 0)
        {
            Refuse(user, "wg-ship-module-refused-empty");
            return false;
        }

        var attempt = new WGShipModuleActivateAttemptEvent(grid, module, user);
        RaiseLocalEvent(ref attempt);
        if (attempt.Cancelled)
        {
            if (attempt.Reason is { } reason)
                Refuse(user, reason);
            return false;
        }

        if (fired)
        {
            if (fitted.Charges >= module.MaxCharges)
                fitted.NextCharge = now + module.RechargeTime;
            fitted.Charges--;
        }

        fitted.ReadyAt = now + module.Cooldown;
        var crew = Crew(grid, user);
        _audio.PlayGlobal(module.ActivateSound, crew, true);
        RaiseNetworkEvent(new WGShipModuleFiredEvent(GetNetEntity(grid), slot, module.Jolt), crew);

        // A switch stays on until pressed again; a timed use for its duration.
        if (!fired || module.Duration > TimeSpan.Zero)
        {
            fitted.Active = true;
            fitted.ActiveUntil = now + module.Duration;
        }

        var ev = new WGShipModuleActivatedEvent(grid, module, user);
        RaiseLocalEvent(ref ev);
        UpdateStrip(grid, bay, now);
        return true;
    }

    /// <summary>Players who hear a ship's module sounds: everyone aboard, and the user wherever they are.</summary>
    public Filter Crew(EntityUid grid, EntityUid? user)
    {
        var crew = Filter.BroadcastGrid(grid);
        if (TryComp<ActorComponent>(user, out var actor))
            crew.AddPlayer(actor.PlayerSession);
        return crew;
    }

    /// <summary>Starts a passive module's effect, with no sound or charge.</summary>
    private void StartEffect(EntityUid grid, WGFittedModule fitted, WGShipModulePrototype module, EntityUid? user)
    {
        fitted.Active = true;
        var ev = new WGShipModuleActivatedEvent(grid, module, user);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Stops a module's effect; <paramref name="audible"/> plays its off sound and lights its key.</summary>
    private void StopEffect(EntityUid grid, WGFittedModule fitted, WGShipModulePrototype module, bool audible,
        EntityUid? user = null, int? slot = null)
    {
        if (!fitted.Active)
            return;

        fitted.Active = false;
        fitted.ActiveUntil = TimeSpan.Zero;
        if (audible && !TerminatingOrDeleted(grid))
        {
            var crew = Crew(grid, user);
            _audio.PlayGlobal(module.DeactivateSound, crew, true);
            RaiseNetworkEvent(new WGShipModuleFiredEvent(GetNetEntity(grid), slot ?? fitted.Slot, 0f), crew);
        }

        var ev = new WGShipModuleDeactivatedEvent(grid, module);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Stops a module that is being taken out or replaced, quietly.</summary>
    private void StopIfActive(EntityUid grid, WGFittedModule fitted)
    {
        if (fitted.Active && _prototype.TryIndex(fitted.Module, out var module))
            StopEffect(grid, fitted, module, false);
    }

    /// <summary>
    /// Switches off a ship's running module, for an effect system that can no longer keep it on. The strip catches up
    /// at the next sweep, so this is safe to call from an effect handler.
    /// </summary>
    public void Deactivate(EntityUid grid, ProtoId<WGShipModulePrototype> module)
    {
        if (FittedSlot(grid, module) is not { } slot
            || GetFitted(grid, slot) is not { } fitted
            || !_prototype.TryIndex(module, out var prototype))
            return;

        StopEffect(grid, fitted, prototype, true);
    }

    private void Refuse(EntityUid? user, LocId reason)
    {
        if (user != null)
            _popup.PopupCursor(Loc.GetString(reason), user.Value);
    }
}
