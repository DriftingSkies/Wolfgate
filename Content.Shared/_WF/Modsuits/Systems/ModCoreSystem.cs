using Content.Shared._WF.Modsuits.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Emp;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.PowerCell.Components;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Modsuits.Systems;

public sealed partial class ModCoreSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModCoreComponent, PowerCellChangedEvent>(OnPowerCellChanged);
        SubscribeLocalEvent<ModCoreComponent, EmpAttemptEvent>(OnEmpAttempt);
        SubscribeLocalEvent<ModCoreRefuelableComponent, InteractUsingEvent>(OnRefuel);
    }

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
