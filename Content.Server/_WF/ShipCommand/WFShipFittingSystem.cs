using System.Linq;
using Content.Server._Mono.SpaceArtillery.Components;
using Content.Server._WF.Drydock;
using Content.Server._WF.Remade.Gifts;
using Content.Server.Cargo.Systems;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Damage.Systems;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._Mono.ShipGuns;
using Content.Shared._WF.Drydock;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Destructible;
using Content.Shared.Doors.Components;
using Content.Shared.Power.Components;
using Content.Shared.Turrets;
using Content.Shared.Examine;
using Content.Shared.Materials;
using Content.Shared.Materials.OreSilo;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// Fits out a commanded ship: best-tier parts in every machine and every ordinary lathe on the ship's ore silo, and
/// optionally hardened guns and doors, bigger gun batteries and faster cranes, in one pass the tick after the ship's
/// machines have built their stock parts; and keeps bottomless storages topped up. A
/// bottomless storage's stock stays in it: it won't eject, drops nothing when the machine is taken apart or destroyed,
/// adds nothing to the machine's price, and what it prints sells for nothing.
/// </summary>
public sealed class WFShipFittingSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ConstructionSystem _construction = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private SharedOreSiloSystem _silos = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private GodmodeSystem _godmode = default!;

    /// <summary>The highest-rated part of each kind, read from the prototypes the first time it's needed.</summary>
    private Dictionary<string, (int Rating, EntProtoId Proto)>? _bestParts;

    private static readonly TimeSpan TopUpInterval = TimeSpan.FromSeconds(5);

    private readonly HashSet<EntityUid> _pending = new();
    private float _topUpTimer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipFittingComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFShipFittingComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFMatterEjectAttemptEvent>(OnEjectAttempt);
        SubscribeLocalEvent<WFBottomlessMaterialsComponent, MachineDeconstructedEvent>(OnBottomlessDeconstructed, before: [typeof(MaterialStorageSystem)]);
        SubscribeLocalEvent<WFBottomlessMaterialsComponent, DestructionEventArgs>(OnBottomlessDestroyed, before: [typeof(MaterialStorageSystem)]);
        SubscribeLocalEvent<WFBottomlessMaterialsComponent, PriceCalculationEvent>(OnBottomlessPrice);
        SubscribeLocalEvent<WFBottomlessMaterialsComponent, WFLatheProducedEvent>(OnBottomlessProduced);
        SubscribeLocalEvent<WFBottomlessPrintComponent, PriceCalculationEvent>(OnPrintPrice);
        SubscribeLocalEvent<WFBottomlessPrintComponent, ExaminedEvent>(OnPrintExamined);
    }

    private void OnMapInit(Entity<WFShipFittingComponent> grid, ref MapInitEvent args)
    {
        _pending.Add(grid);
    }

    /// <summary>Added to a grid already running (an admin vessel's added components): fit it next tick.</summary>
    private void OnStartup(Entity<WFShipFittingComponent> grid, ref ComponentStartup args)
    {
        if (MetaData(grid).EntityLifeStage >= EntityLifeStage.MapInitialized)
            _pending.Add(grid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_pending.Count > 0)
        {
            foreach (var grid in _pending)
            {
                if (TryComp<WFShipFittingComponent>(grid, out var fitting) && !fitting.Fitted)
                    Fit((grid, fitting));
            }

            _pending.Clear();
        }

        _topUpTimer += frameTime;
        if (_topUpTimer < (float) TopUpInterval.TotalSeconds)
            return;

        _topUpTimer = 0f;
        var query = EntityQueryEnumerator<WFBottomlessMaterialsComponent, MaterialStorageComponent>();
        while (query.MoveNext(out var uid, out var bottomless, out var storage))
            TopUp((uid, bottomless, storage));
    }

    /// <summary>Runs the fitting pass on a ship now. Returns how many machines got new parts.</summary>
    public int Fit(Entity<WFShipFittingComponent> grid)
    {
        grid.Comp.Fitted = true;
        var upgraded = 0;
        EntityUid? silo = null;
        var clients = new List<EntityUid>();
        // Collected first: new parts are spawned on the grid before they go into their machines.
        var aboard = new List<EntityUid>();
        var children = Transform(grid).ChildEnumerator;
        while (children.MoveNext(out var child))
            aboard.Add(child);

        foreach (var child in aboard)
        {
            if (grid.Comp.MaxParts && TryComp<MachineComponent>(child, out var machine) && UpgradeParts((child, machine)))
                upgraded++;

            if (HasComp<OreSiloComponent>(child))
                silo ??= child;
            else if (HasComp<OreSiloClientComponent>(child) && !HasComp<WFBottomlessMaterialsComponent>(child))
                clients.Add(child);

            FitArms(grid.Comp, child);
        }

        if (grid.Comp.LinkSilo && silo is { } s)
        {
            foreach (var client in clients)
                _silos.TryLinkClient(s, client, grid.Comp.SiloRange);
        }

        return upgraded;
    }

    /// <summary>Hardens and charges one entity aboard: guns, hardpoints, turrets, doors and cranes.</summary>
    private void FitArms(WFShipFittingComponent fitting, EntityUid child)
    {
        var isGun = HasComp<ShipGunTypeComponent>(child);
        if (fitting.IndestructibleArms && (isGun || HasComp<HardpointComponent>(child)
            || HasComp<DeployableTurretComponent>(child) || HasComp<WFShipSentryComponent>(child)))
            _godmode.EnableGodmode(child);

        if (fitting.UnpryableDoors && TryComp<DoorComponent>(child, out var door) && door.CanPry)
        {
            door.CanPry = false;
            Dirty(child, door);
        }

        if (isGun)
        {
            if (TryComp<BatteryComponent>(child, out var battery) && !MathHelper.CloseTo(fitting.GunChargeScale, 1f))
            {
                _battery.SetMaxCharge(child, battery.MaxCharge * fitting.GunChargeScale, battery);
                _battery.SetCharge(child, battery.MaxCharge, battery);
            }

            if (!MathHelper.CloseTo(fitting.GunRechargeScale, 1f))
            {
                if (TryComp<BatterySelfRechargerComponent>(child, out var recharger))
                    recharger.AutoRechargeRate *= fitting.GunRechargeScale;
                if (TryComp<SpaceArtilleryComponent>(child, out var artillery))
                    artillery.PowerChargeRate = (int) (artillery.PowerChargeRate * fitting.GunRechargeScale);
            }
        }

        if (fitting.CraneSpeed is { } speed && TryComp<WFDrydockCraneComponent>(child, out var crane)
            && !HasComp<WFDrydockCraneUpgradesComponent>(child))
        {
            crane.SpeedMultiplier = speed;
            Dirty(child, crane);
        }
    }

    /// <summary>Swaps each of a machine's parts for the best part of its kind. False when nothing changed.</summary>
    public bool UpgradeParts(Entity<MachineComponent> machine)
    {
        var swaps = new List<(EntityUid Old, EntProtoId New)>();
        foreach (var part in machine.Comp.PartContainer.ContainedEntities)
        {
            if (TryComp<MachinePartComponent>(part, out var comp)
                && BestParts().TryGetValue(comp.PartType.Id, out var best)
                && comp.Rating < best.Rating)
                swaps.Add((part, best.Proto));
        }

        if (swaps.Count == 0)
            return false;

        foreach (var (old, best) in swaps)
        {
            var coords = Transform(machine).Coordinates;
            var fresh = Spawn(best, coords);
            _container.Remove(old, machine.Comp.PartContainer);
            QueueDel(old);
            _container.Insert(fresh, machine.Comp.PartContainer);
        }

        _construction.RefreshParts(machine, machine.Comp);
        return true;
    }

    /// <summary>For each part kind, the spawnable part prototype with the highest rating.</summary>
    private Dictionary<string, (int Rating, EntProtoId Proto)> BestParts()
    {
        if (_bestParts != null)
            return _bestParts;

        _bestParts = new();
        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || !proto.TryGetComponent<MachinePartComponent>(out var part, _factory))
                continue;

            var kind = part.PartType.Id;
            if (!_bestParts.TryGetValue(kind, out var best) || part.Rating > best.Rating)
                _bestParts[kind] = (part.Rating, proto.ID);
        }

        return _bestParts;
    }

    /// <summary>Tops every material a bottomless storage can hold back up to its amount.</summary>
    private void TopUp(Entity<WFBottomlessMaterialsComponent, MaterialStorageComponent> storage)
    {
        foreach (var material in _proto.EnumeratePrototypes<MaterialPrototype>())
        {
            var have = _materials.GetMaterialAmount(storage, material.ID, storage.Comp2);
            var missing = storage.Comp1.Amount - have;
            if (missing > 0)
                _materials.TryChangeMaterialAmount(storage, material.ID, missing, storage.Comp2, dirty: true, localOnly: true);
        }
    }

    /// <summary>The material storage's eject handler asks first: a bottomless storage's stock never comes out as sheets.</summary>
    private void OnEjectAttempt(ref WFMatterEjectAttemptEvent args)
    {
        if (args.Cancelled || !HasComp<WFBottomlessMaterialsComponent>(args.Storage))
            return;

        args.Cancelled = true;
        _popup.PopupEntity(Loc.GetString("wf-shipcommand-bottomless-no-eject"), args.Storage, args.User);
    }

    private void OnBottomlessDeconstructed(Entity<WFBottomlessMaterialsComponent> ent, ref MachineDeconstructedEvent args)
    {
        Empty(ent);
    }

    private void OnBottomlessDestroyed(Entity<WFBottomlessMaterialsComponent> ent, ref DestructionEventArgs args)
    {
        Empty(ent);
    }

    /// <summary>Empties a bottomless storage about to drop its contents, so its stock doesn't spill out as sheets.</summary>
    private void Empty(EntityUid uid)
    {
        if (!TryComp<MaterialStorageComponent>(uid, out var storage))
            return;

        foreach (var (material, amount) in storage.Storage.ToList())
            _materials.TryChangeMaterialAmount(uid, material.Id, -amount, storage, localOnly: true);
    }

    /// <summary>Takes back what the material storage added to the price for a bottomless stock.</summary>
    private void OnBottomlessPrice(Entity<WFBottomlessMaterialsComponent> ent, ref PriceCalculationEvent args)
    {
        if (!TryComp<MaterialStorageComponent>(ent, out var storage))
            return;

        foreach (var (material, amount) in storage.Storage)
        {
            if (_proto.TryIndex<MaterialPrototype>(material, out var proto))
                args.Price -= proto.Price * amount;
        }
    }

    private void OnBottomlessProduced(Entity<WFBottomlessMaterialsComponent> ent, ref WFLatheProducedEvent args)
    {
        EnsureComp<WFBottomlessPrintComponent>(args.Product);
    }

    private void OnPrintPrice(Entity<WFBottomlessPrintComponent> ent, ref PriceCalculationEvent args)
    {
        args.Price = 0;
        args.Handled = true;
    }

    private void OnPrintExamined(Entity<WFBottomlessPrintComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-shipcommand-bottomless-print-examine"));
    }
}
