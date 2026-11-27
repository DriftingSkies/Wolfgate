using Content.Server._WG.ShipModules;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.ShipModules;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Drydock;

/// <summary>
/// Ship modules at the drydock: a fitting is a crane job sold like a refit, keyed by <see cref="WGShipModuleFitting"/>;
/// emptying a slot is free and immediate.
/// </summary>
public sealed partial class WGDrydockSystem
{
    [Dependency] private WGShipModuleSystem _shipModules = default!;

    private void InitializeModules()
    {
        SubscribeLocalEvent<WGDrydockConsoleComponent, WGDrydockRemoveModuleMessage>(OnRemoveModule);
    }

    /// <summary>Whether a console fits ship modules: any drydock that sells refits.</summary>
    private static bool FitsModules(WGDrydockConsoleComponent comp)
    {
        return comp.Refits.Count > 0;
    }

    /// <summary>Lists the work to fit a module into a slot, or the reason it can't be sold.</summary>
    private string? BuildModuleWork(EntityUid ship, WGDrydockConsoleComponent comp, int slot, string moduleId,
        out List<WGDrydockWorkItem> items)
    {
        items = new List<WGDrydockWorkItem>();
        if (!FitsModules(comp)
            || slot < 0
            || slot >= WGShipModulesComponent.SlotCount
            || !_prototype.TryIndex<WGShipModulePrototype>(moduleId, out var module)
            || module.Unlisted)
            return Loc.GetString("drydock-console-unknown-refit");

        if (_shipModules.FittedSlot(ship, module.ID) is { } fittedSlot)
        {
            return fittedSlot == slot
                ? Loc.GetString("drydock-module-already-fitted")
                : Loc.GetString("drydock-module-fitted-elsewhere", ("slot", fittedSlot + 1));
        }

        // Fitting replaces the slot's module, so a taken slot is emptied first rather than losing a paid-for module.
        if (_shipModules.GetFitted(ship, slot) != null)
            return Loc.GetString("drydock-module-slot-taken", ("slot", slot + 1));

        if (!_shipModules.HasKitFor(ship, module))
            return Loc.GetString("drydock-module-needs-kit", ("kit", KitName(module)));

        var item = ModuleItem(ship, slot, module);
        item.LocalPos = HelmPosition(ship);
        items.Add(item);
        return null;
    }

    /// <summary>The one work item that fits a module; the caller places it for the cranes.</summary>
    private static WGDrydockWorkItem ModuleItem(EntityUid ship, int slot, WGShipModulePrototype module)
    {
        var materials = new Dictionary<string, int>();
        foreach (var (material, amount) in module.Materials)
        {
            materials[material.Id] = amount;
        }

        return new WGDrydockWorkItem
        {
            Kind = WGDrydockWorkKind.Upgrade,
            Target = ship,
            Upgrade = WGShipModuleFitting.Key(slot, module.ID),
            Cost = module.Price,
            RefitCost = module.Price,
            RefitMaterials = materials,
        };
    }

    /// <summary>Where the cranes work a module fitting: the ship's helm, or the middle of the hull without one.</summary>
    private System.Numerics.Vector2 HelmPosition(EntityUid ship)
    {
        var helms = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (helms.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == ship && xform.ParentUid == ship)
                return xform.LocalPosition;
        }

        return TryComp<MapGridComponent>(ship, out var grid) ? grid.LocalAABB.Center : default;
    }

    /// <summary>Fits a finished module job's module, if the item is one.</summary>
    private bool TryApplyModule(WGDrydockWorkItem item)
    {
        if (!WGShipModuleFitting.TryParse(item.Upgrade, out var slot, out var module))
            return false;

        if (!TerminatingOrDeleted(item.Target) && _prototype.TryIndex<WGShipModulePrototype>(module, out var prototype))
        {
            // The kit goes into the ship with the module.
            if (prototype.Kit != null && _shipModules.FindKit(item.Target, prototype.ID) is { } kit)
                QueueDel(kit);
            _shipModules.Fit(item.Target, slot, module);
        }

        return true;
    }

    /// <summary>The name of the kit a module needs, or its id if the kit prototype is missing.</summary>
    private string KitName(WGShipModulePrototype module)
    {
        return module.Kit is { } kit && _prototype.TryIndex(kit, out var prototype) ? prototype.Name : module.Kit?.Id ?? string.Empty;
    }

    /// <summary>
    /// Whether a finished fitting can no longer go in: since the job started the slot took another module, or the
    /// module went into another slot. The job gives it back rather than fit over a paid-for module or fit nothing.
    /// </summary>
    private bool ModuleRefused(WGDrydockWorkItem item)
    {
        if (item.Kind != WGDrydockWorkKind.Upgrade
            || !WGShipModuleFitting.TryParse(item.Upgrade, out var slot, out var module)
            || TerminatingOrDeleted(item.Target))
            return false;

        // A kit taken off the ship during the job leaves nothing to fit.
        return !_prototype.TryIndex<WGShipModulePrototype>(module, out var prototype)
               || _shipModules.FittedSlot(item.Target, module) is { } other && other != slot
               || _shipModules.GetFitted(item.Target, slot) is { } there && there.Module.Id != module
               || _shipModules.GetFitted(item.Target, slot) == null && !_shipModules.HasKitFor(item.Target, prototype);
    }

    /// <summary>The service record's name for a work order: the module for a fitting, else the refit id or a repair.</summary>
    private string ServiceName(string? upgrade)
    {
        if (WGShipModuleFitting.TryParse(upgrade, out _, out var moduleId)
            && _prototype.TryIndex<WGShipModulePrototype>(moduleId, out var module))
            return Loc.GetString(module.Name);

        return upgrade ?? Loc.GetString("wg-ship-service-repair");
    }

    /// <summary>Adds the ship's slots and the modules on sale to a console state.</summary>
    private void AddModuleState(EntityUid console, WGDrydockConsoleState state, EntityUid? ship, WGDrydockConsoleComponent comp,
        WGDrydockMaterialPaymentComponent? pay)
    {
        if (!FitsModules(comp))
            return;

        state.ShipModules = true;
        for (var slot = 0; slot < WGShipModulesComponent.SlotCount; slot++)
        {
            state.FittedModules.Add(ship is { } fitted ? _shipModules.GetFitted(fitted, slot)?.Module.Id : null);
        }

        var modules = new List<WGShipModulePrototype>(_prototype.EnumeratePrototypes<WGShipModulePrototype>());
        modules.Sort((a, b) => string.CompareOrdinal(a.ID, b.ID));
        foreach (var module in modules)
        {
            if (module.Unlisted)
                continue;

            var entry = new WGDrydockModuleEntry { Id = module.ID };
            if (ship is { } shipUid)
            {
                entry.KitAboard = module.Kit != null && _shipModules.FindKit(shipUid, module.ID) != null;
                var items = new List<WGDrydockWorkItem> { ModuleItem(shipUid, 0, module) };
                entry.Price = WorkPrice(console, shipUid, comp, items[0].Upgrade, items);
                if (pay != null)
                    entry.Materials = RefitBill(pay, items[0]);
            }

            state.Modules.Add(entry);
        }
    }

    /// <summary>Empties a slot of the berthed ship. Only its deed holder may, on a ship with a deed.</summary>
    private void OnRemoveModule(EntityUid uid, WGDrydockConsoleComponent comp, WGDrydockRemoveModuleMessage args)
    {
        if (WalkInLocked(uid, comp, args.Actor) || !FitsModules(comp))
            return;

        if (GetBerthedShip(uid, comp) is not { } ship)
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-no-ship"));
            return;
        }

        if (HasComp<WGDrydockJobComponent>(uid))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-busy"));
            return;
        }

        if (TryComp<WGDrydockBookingComponent>(uid, out var booking) && booking.Ship != ship)
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-console-reserved", ("berth", BerthLabel(uid, comp)), ("ship", Name(booking.Ship))));
            return;
        }

        if (TryComp<ShuttleDeedComponent>(ship, out var deed) && deed.ShuttleOwner != Name(args.Actor))
        {
            Deny(uid, comp, args.Actor, Loc.GetString("drydock-module-not-owner"));
            return;
        }

        if (_shipModules.GetFitted(ship, args.Slot) is not { } fitted)
            return;

        var name = _prototype.TryIndex(fitted.Module, out var module) ? Loc.GetString(module.Name) : fitted.Module.Id;
        _shipModules.Unfit(ship, args.Slot);
        _popup.PopupCursor(Loc.GetString("drydock-module-removed", ("module", name), ("slot", args.Slot + 1)), args.Actor);
        UpdateUi(uid, comp, true);
    }
}
