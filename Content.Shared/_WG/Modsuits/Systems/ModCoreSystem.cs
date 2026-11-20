using Content.Shared._WG.Modsuits.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emp;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;

namespace Content.Shared._WG.Modsuits.Systems;

public sealed partial class ModCoreSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModCoreComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<ModCoreComponent, EmpAttemptEvent>(OnEmpAttempt);
        SubscribeLocalEvent<ModCoreComponent, EntInsertedIntoContainerMessage>(OnCellInserted);
        SubscribeLocalEvent<ModCoreComponent, EntRemovedFromContainerMessage>(OnCellRemoved);
        SubscribeLocalEvent<ModCoreComponent, InteractUsingEvent>(OnCellInteractUsing);
        SubscribeLocalEvent<ModCoreRefuelableComponent, InteractUsingEvent>(OnRefuel);
    }

    [Dependency] private INetManager _net = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBatterySystem _battery = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    private void OnPowerCellChanged(Entity<ModCoreComponent> ent, ref PowerCellChangedEvent args)
    {
        RelayToSuit(ent, ref args);
    }
    private void OnEmpAttempt(Entity<ModCoreComponent> ent, ref EmpAttemptEvent args)
    {
        RelayToSuit(ent, ref args);
    }

    /// <summary>
    /// Inserts cells on the server only. A predicted insertion into the core's container slot made large cells
    /// such as power cages collide with the container state when the server confirmed them.
    /// </summary>
    private void OnCellInteractUsing(Entity<ModCoreComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || _net.IsClient)
            return;

        if (!TryComp<PowerCellSlotComponent>(ent, out var slotInfo)
            || !_itemSlots.TryGetSlot(ent.Owner, slotInfo.CellSlotId, out var slot))
            return;

        // Leave other items unhandled so bags, labelers and price guns still work on a loose core.
        if (!HasComp<PowerCellComponent>(args.Used))
            return;

        args.Handled = true;

        if (slot.HasItem)
        {
            _popup.PopupEntity(Loc.GetString("modsuit-cell-slot-occupied"), ent, args.User);
            return;
        }

        if (!_itemSlots.TryInsert(ent.Owner, slot, args.Used, args.User))
        {
            var message = slot.WhitelistFailPopup is { } popup
                ? Loc.GetString(popup)
                : Loc.GetString("modsuit-cell-slot-wrong-item");
            _popup.PopupEntity(message, ent, args.User);
        }
    }

    private void OnCellInserted(Entity<ModCoreComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (_net.IsClient
            || !TryComp<PowerCellSlotComponent>(ent, out var slot)
            || args.Container.ID != slot.CellSlotId)
            return;

        ScaleCell(ent, args.Entity);
    }

    private void OnCellRemoved(Entity<ModCoreComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (_net.IsClient
            || !TryComp<PowerCellSlotComponent>(ent, out var slot)
            || args.Container.ID != slot.CellSlotId)
            return;

        RestoreCell(args.Entity);
    }

    /// <summary>Gives a MOD cell the TG station capacity for its tier while it sits in the core.</summary>
    private void ScaleCell(Entity<ModCoreComponent> core, EntityUid cell)
    {
        if (HasComp<ModsuitCellComponent>(cell) || !HasComp<BatteryComponent>(cell))
            return;

        var ev = new GetChargeEvent();
        RaiseLocalEvent(cell, ref ev);

        var target = core.Comp.DefaultCellCharge;
        if (TryComp<MachinePartComponent>(cell, out var part)
            && part.Rating >= 1
            && part.Rating <= core.Comp.CellChargeByRating.Count)
            target = core.Comp.CellChargeByRating[part.Rating - 1];

        if (target <= 0f || MathF.Abs(target - ev.MaxCharge) < 0.01f)
            return;

        var marker = AddComp<ModsuitCellComponent>(cell);
        marker.OriginalMaxCharge = ev.MaxCharge;
        marker.ScaledMaxCharge = target;

        var ratio = ev.MaxCharge > 0f ? ev.CurrentCharge / ev.MaxCharge : 0f;
        _battery.SetMaxCharge(cell, target);
        _battery.ChangeCharge(cell, ratio * target - GetCharge(cell));
    }

    /// <summary>Puts the cell's real capacity and charge back when it leaves the core.</summary>
    private void RestoreCell(EntityUid cell)
    {
        if (!TryComp<ModsuitCellComponent>(cell, out var marker))
            return;

        var ev = new GetChargeEvent();
        RaiseLocalEvent(cell, ref ev);
        var ratio = marker.ScaledMaxCharge > 0f ? ev.CurrentCharge / marker.ScaledMaxCharge : 0f;

        _battery.SetMaxCharge(cell, marker.OriginalMaxCharge);
        _battery.ChangeCharge(cell, ratio * marker.OriginalMaxCharge - GetCharge(cell));
        RemComp<ModsuitCellComponent>(cell);
    }

    private float GetCharge(EntityUid uid)
    {
        var ev = new GetChargeEvent();
        RaiseLocalEvent(uid, ref ev);
        return ev.CurrentCharge;
    }

    private void RelayToSuit<T>(Entity<ModCoreComponent> ent, ref T args) where T : notnull
    {
        if (_container.TryGetContainingContainer((ent.Owner, null, null), out var container)
            && HasComp<ModSuitComponent>(container.Owner))
            RaiseLocalEvent(container.Owner, ref args);
    }

    /// <summary>Finds a battery on the core or in the power cell slot it owns.</summary>
    private Entity<BatteryComponent>? GetBattery(EntityUid core)
    {
        if (TryComp<BatteryComponent>(core, out var battery))
            return (core, battery);

        if (!TryComp<PowerCellSlotComponent>(core, out var cellSlot)
            || _itemSlots.GetItemOrNull(core, cellSlot.CellSlotId) is not { } cell
            || !TryComp<BatteryComponent>(cell, out var cellBattery))
            return null;

        return (cell, cellBattery);
    }
    private void OnRefuel(Entity<ModCoreRefuelableComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (ent.Comp.FuelPrototypes.Count > 0)
        {
            if (MetaData(args.Used).EntityPrototype?.ID is not { } usedId || !ent.Comp.FuelPrototypes.Contains(usedId))
                return;
        }
        else if (!_whitelist.IsValid(ent.Comp.Fuel, args.Used))
            return;

        args.Handled = true;

        if (GetBattery(ent.Owner) is not { } battery)
            return;

        var ev = new GetChargeEvent();
        RaiseLocalEvent(battery.Owner, ref ev);

        var missing = battery.Comp.MaxCharge - ev.CurrentCharge;
        if (missing <= 0f)
        {
            _popup.PopupClient(Loc.GetString("modsuit-core-full"), args.Target, args.User);
            return;
        }

        var wanted = Math.Max(1, (int) MathF.Ceiling(missing / ent.Comp.ChargePerFuel));
        var used = 1;
        if (TryComp<StackComponent>(args.Used, out var stack))
        {
            used = Math.Min(wanted, stack.Count);
            _stack.SetCount(args.Used, stack.Count - used, stack);
        }
        else
        {
            PredictedQueueDel(args.Used);
        }

        _battery.ChangeCharge(battery.AsNullable(), used * ent.Comp.ChargePerFuel);
        _audio.PlayPredicted(ent.Comp.RefuelSound, args.Target, args.User);
        _popup.PopupClient(Loc.GetString("modsuit-core-refueled"), args.Target, args.User);
    }
}
