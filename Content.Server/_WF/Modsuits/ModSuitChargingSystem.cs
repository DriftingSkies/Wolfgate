using Content.Server.Power.EntitySystems;
using Content.Shared._WF.Modsuits.Components;
using Content.Shared._WF.Modsuits.Systems;
using Content.Shared.Inventory;

namespace Content.Server._WF.Modsuits;

/// <summary>Lets a cyborg recharger find and charge the core battery of a worn MODsuit.</summary>
public sealed partial class ModSuitChargingSystem : EntitySystem
{
    [Dependency] private ModSuitSystem _modSuit = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSuitComponent, InventoryRelayedEvent<FindInventoryBatteryEvent>>(OnFindInventoryBattery);
    }

    private void OnFindInventoryBattery(Entity<ModSuitComponent> ent, ref InventoryRelayedEvent<FindInventoryBatteryEvent> args)
    {
        if (args.Args.FoundBattery != null)
            return;

        if (_modSuit.GetBattery(ent) is { } battery)
            args.Args.FoundBattery = battery;
    }
}
