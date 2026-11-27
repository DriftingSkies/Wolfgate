using Content.Shared._WG.ShipModules;

namespace Content.Server._WG.ShipModules;

/// <summary>Fits the modules that mapping markers name when their ship spawns, then deletes the markers.</summary>
public sealed partial class WGShipModulePresetSystem : EntitySystem
{
    [Dependency] private WGShipModuleSystem _modules = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipModulePresetComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WGShipModulePresetComponent> ent, ref MapInitEvent args)
    {
        if (Transform(ent).GridUid is { } grid && _modules.FittedSlot(grid, ent.Comp.Module) == null)
        {
            var slot = ent.Comp.Slot is { } hotkey ? hotkey - 1 : FreeSlot(grid);
            if (slot is not { } index || _modules.GetFitted(grid, index) != null || !_modules.Fit(grid, index, ent.Comp.Module))
                Log.Warning($"{ToPrettyString(ent)} couldn't fit {ent.Comp.Module} on {ToPrettyString(grid)}: slot {ent.Comp.Slot?.ToString() ?? "any"} isn't free.");
        }

        QueueDel(ent);
    }

    /// <summary>The first empty slot on a ship, or null when all are filled.</summary>
    private int? FreeSlot(EntityUid grid)
    {
        for (var slot = 0; slot < WGShipModulesComponent.SlotCount; slot++)
        {
            if (_modules.GetFitted(grid, slot) == null)
                return slot;
        }

        return null;
    }
}
