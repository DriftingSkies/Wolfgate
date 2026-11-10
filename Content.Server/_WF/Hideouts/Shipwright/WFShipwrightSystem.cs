using System.Linq;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._Crescent.Hardpoints;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Hideouts.Shipwright;
using Content.Shared._WF.Outposts;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Hideouts.Shipwright;

/// <summary>
/// Shipwright ships: what a hull's guns and systems take of its refit points, which of them the shipwright fitted, and
/// whether the hull passes. A gun the shipwright didn't fit won't fire, and a hull that doesn't pass isn't saved.
/// </summary>
public sealed partial class WFShipwrightSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private WFHideoutSystem _hideouts = default!;

    /// <summary>Each system's marking component types, in system order.</summary>
    private readonly List<(WFShipwrightSystemPrototype System, Type[] Types)> _systems = new();

    /// <summary>The guns the console sells, built once.</summary>
    private List<WFShipwrightGunView>? _guns;

    /// <summary>The hardpoints the console lays, built once.</summary>
    private List<WFShipwrightMountTypeView>? _mountTypes;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HardpointAnchorableOnlyComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<WFShipwrightShipComponent, WFHideoutShipSaveAttemptEvent>(OnSaveAttempt);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        InitializeConsole();
        LoadSystems();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WFShipwrightSystemPrototype>())
            LoadSystems();
        if (args.WasModified<EntityPrototype>())
        {
            _guns = null;
            _mountTypes = null;
        }
    }

    private void LoadSystems()
    {
        _systems.Clear();
        foreach (var system in _proto.EnumeratePrototypes<WFShipwrightSystemPrototype>().OrderBy(s => s.Order).ThenBy(s => s.ID))
        {
            var types = new List<Type>();
            foreach (var name in system.Components)
            {
                if (_factory.TryGetRegistration(name, out var registration))
                    types.Add(registration.Type);
                else
                    Log.Error($"Shipwright system {system.ID} names unknown component {name}.");
            }

            _systems.Add((system, types.ToArray()));
        }
    }

    public WFShipwrightRulesPrototype Rules => _proto.Index<WFShipwrightRulesPrototype>(WFShipwrightRulesPrototype.Default);

    /// <summary>Refit points a hardpoint takes, by its size, unless it came with the frame.</summary>
    public int MountPoints(EntityUid uid, HardpointComponent hardpoint)
    {
        return HasComp<WFShipwrightStockComponent>(uid) ? 0 : Rules.MountPoints.GetValueOrDefault(hardpoint.CompatibleSizes);
    }

    /// <summary>Refit points a gun takes, by the hardpoint size it needs.</summary>
    public int WeaponPoints(weaponSizes size)
    {
        return Rules.WeaponPoints.GetValueOrDefault(size);
    }

    /// <summary>The system an entity counts as, if any.</summary>
    public WFShipwrightSystemPrototype? SystemOf(EntityUid uid)
    {
        if (MetaData(uid).EntityPrototype is { } proto)
        {
            foreach (var (system, _) in _systems)
            {
                if (system.Prototypes.Contains(proto.ID))
                    return system;
            }
        }

        foreach (var (system, types) in _systems)
        {
            foreach (var type in types)
            {
                if (HasComp(uid, type))
                    return system;
            }
        }

        return null;
    }

    /// <summary>The system an entity prototype counts as, if any.</summary>
    public WFShipwrightSystemPrototype? SystemOf(EntityPrototype proto)
    {
        foreach (var (system, _) in _systems)
        {
            if (system.Prototypes.Contains(proto.ID))
                return system;
        }

        foreach (var (system, types) in _systems)
        {
            foreach (var type in types)
            {
                if (proto.Components.ContainsKey(_factory.GetComponentName(type)))
                    return system;
            }
        }

        return null;
    }

    /// <summary>The most refit points any shipwright ship may have: the largest frame's.</summary>
    public int Ceiling()
    {
        var ceiling = 0;
        foreach (var frame in _proto.EnumeratePrototypes<WFShipwrightFramePrototype>())
            ceiling = Math.Max(ceiling, frame.Points);
        return ceiling;
    }

    /// <summary>Refit points the outpost's built levels add to its shipwright ship.</summary>
    public int BayPoints(EntityUid outpost)
    {
        if (!TryComp<WFHideoutComponent>(outpost, out var hideout))
            return 0;

        var points = 0;
        foreach (var (id, level) in hideout.Levels)
        {
            if (!_proto.TryIndex<WFHideoutModulePrototype>(id, out var module))
                continue;

            for (var i = 0; i < level && i < module.Levels.Count; i++)
            {
                points += module.Levels[i].ShipwrightPoints;
            }
        }

        return points;
    }

    /// <summary>
    /// Counts what a hull carries against its refit points. <paramref name="outpost"/> is the outpost whose bay it stands
    /// in, for the bay's bonus; without one, the hull's owner's outpost counts.
    /// </summary>
    public WFShipwrightSurvey Survey(EntityUid hull, EntityUid? outpost = null)
    {
        var survey = new WFShipwrightSurvey();
        var ship = CompOrNull<WFShipwrightShipComponent>(hull);
        survey.Frame = ship?.Frame;
        if (TryComp<MapGridComponent>(hull, out var grid))
        {
            var tiles = EntityManager.System<SharedMapSystem>().GetAllTilesEnumerator(hull, grid);
            while (tiles.MoveNext(out _))
                survey.Tiles++;
        }

        var rules = Rules;
        if (survey.Frame != null && _proto.TryIndex<WFShipwrightFramePrototype>(survey.Frame, out var frame))
            survey.Budget = frame.Points;
        else
            survey.Budget = rules.CustomPoints + Math.Min(rules.SizePointsCap, survey.Tiles / Math.Max(1, rules.TilesPerPoint));

        if (outpost == null && TryComp<WFHideoutShipComponent>(hull, out var owned) && _outposts.TryGetOutpost(owned.Owner, out var home))
            outpost = home;
        if (outpost != null)
            survey.Budget += BayPoints(outpost.Value);

        // No custom ship goes past the largest frame.
        survey.Budget = Math.Min(survey.Budget, Ceiling());

        var pending = new Stack<EntityUid>();
        pending.Push(hull);
        while (pending.TryPop(out var parent))
        {
            var children = Transform(parent).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                pending.Push(child);
                Count(survey, child);
            }
        }

        var stock = survey.Stock;
        survey.MaxMounts = Math.Max(stock, rules.BaseMounts + survey.Tiles / Math.Max(1, rules.TilesPerMount));

        foreach (var (system, _) in _systems)
        {
            if (system.Limit > 0 && survey.Systems.GetValueOrDefault(system.ID) + survey.Unfitted.GetValueOrDefault(system.ID) > system.Limit)
                survey.OverLimit.Add(system.ID);
        }

        return survey;
    }

    private void Count(WFShipwrightSurvey survey, EntityUid uid)
    {
        var fitted = HasComp<WFShipwrightFittedComponent>(uid);
        if (TryComp<HardpointComponent>(uid, out var hardpoint))
        {
            survey.Mounts++;
            if (HasComp<WFShipwrightStockComponent>(uid))
                survey.Stock++;
            var mountPoints = MountPoints(uid, hardpoint);
            if (fitted || HasComp<WFShipwrightStockComponent>(uid))
                survey.Spent += mountPoints;
            else
                survey.UnfittedMounts.Add((uid, mountPoints));
            return;
        }

        var system = SystemOf(uid);
        if (system == null)
        {
            if (!TryComp<HardpointAnchorableOnlyComponent>(uid, out var gun))
                return;

            var points = WeaponPoints(gun.CompatibleSizes);
            if (fitted)
                survey.Spent += points;
            else
                survey.UnfittedWeapons.Add((uid, points));
            return;
        }

        if (fitted)
        {
            survey.Spent += system.Points;
            survey.Systems[system.ID] = survey.Systems.GetValueOrDefault(system.ID) + 1;
        }
        else
        {
            survey.Unfitted[system.ID] = survey.Unfitted.GetValueOrDefault(system.ID) + 1;
            survey.UnfittedSystems.Add((uid, system.Points));
        }
    }

    private void OnShotAttempted(Entity<HardpointAnchorableOnlyComponent> gun, ref ShotAttemptedEvent args)
    {
        // On a shipwright ship, only what the shipwright fitted fires.
        if (Transform(gun).GridUid is { } grid && HasComp<WFShipwrightShipComponent>(grid) && !HasComp<WFShipwrightFittedComponent>(gun))
            args.Cancel();
    }

    private void OnSaveAttempt(Entity<WFShipwrightShipComponent> hull, ref WFHideoutShipSaveAttemptEvent args)
    {
        var survey = Survey(hull);
        if (!survey.Certified)
            args.Refusal = survey.Spent > survey.Budget ? "wf-shipwright-over-budget"
                : survey.OverLimit.Count > 0 ? "wf-shipwright-over-limit"
                : "wf-shipwright-uncertified";
    }

    /// <summary>The hardpoints the console lays: every hardpoint but the debug ones, at the rules' price for its size.</summary>
    public List<WFShipwrightMountTypeView> MountTypes()
    {
        if (_mountTypes != null)
            return _mountTypes;

        var rules = Rules;
        var name = _factory.GetComponentName<HardpointComponent>();
        _mountTypes = new List<WFShipwrightMountTypeView>();
        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || proto.ID.Contains("Debug") || !proto.TryGetComponent<HardpointComponent>(name, out var hardpoint))
                continue;

            _mountTypes.Add(new WFShipwrightMountTypeView(proto.ID, proto.Name, (int) hardpoint.CompatibleSizes,
                (int) hardpoint.CompatibleTypes, rules.MountPoints.GetValueOrDefault(hardpoint.CompatibleSizes),
                rules.MountPrices.GetValueOrDefault(hardpoint.CompatibleSizes)));
        }

        _mountTypes.Sort((a, b) => a.Size != b.Size ? a.Size.CompareTo(b.Size) : a.Types.CompareTo(b.Types));
        return _mountTypes;
    }

    /// <summary>The guns the console sells: every hardpoint gun with a price.</summary>
    public List<WFShipwrightGunView> Guns()
    {
        if (_guns != null)
            return _guns;

        var rules = Rules;
        var name = _factory.GetComponentName<HardpointAnchorableOnlyComponent>();
        _guns = new List<WFShipwrightGunView>();
        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>())
        {
            if (proto.Abstract || !proto.TryGetComponent<HardpointAnchorableOnlyComponent>(name, out var gun))
                continue;

            var price = (int) Math.Ceiling(_pricing.GetEstimatedPrice(proto) * rules.WeaponMarkup);
            if (price <= 0)
                continue;

            // A mounted system, such as a mining laser, takes its system's points and price.
            var system = SystemOf(proto);
            _guns.Add(new WFShipwrightGunView(proto.ID, proto.Name, (int) gun.CompatibleSizes, (int) gun.CompatibleTypes,
                system?.Points ?? WeaponPoints(gun.CompatibleSizes), system?.Price ?? price + rules.Labour));
        }

        _guns.Sort((a, b) => a.Size != b.Size ? a.Size.CompareTo(b.Size) : string.CompareOrdinal(a.Name, b.Name));
        return _guns;
    }
}

/// <summary>What a hull carries against its refit points.</summary>
public sealed class WFShipwrightSurvey
{
    public string? Frame;
    public int Tiles;
    public int Budget;

    /// <summary>Points the fitted guns and systems take.</summary>
    public int Spent;

    /// <summary>Fitted systems by id.</summary>
    public readonly Dictionary<string, int> Systems = new();

    /// <summary>Systems on the hull the shipwright didn't fit, by id.</summary>
    public readonly Dictionary<string, int> Unfitted = new();

    public readonly List<(EntityUid Uid, int Points)> UnfittedWeapons = new();

    /// <summary>Hardpoints on a hand-built hull the shipwright didn't lay.</summary>
    public readonly List<(EntityUid Uid, int Points)> UnfittedMounts = new();

    /// <summary>Hardpoints on the hull, those that came with its frame, and the most it may carry.</summary>
    public int Mounts;
    public int Stock;
    public int MaxMounts;
    public readonly List<(EntityUid Uid, int Points)> UnfittedSystems = new();

    /// <summary>Systems the hull carries more of than they allow.</summary>
    public readonly List<string> OverLimit = new();

    /// <summary>Points the unfitted guns and systems would take if certified.</summary>
    public int UnfittedPoints => UnfittedWeapons.Sum(w => w.Points) + UnfittedSystems.Sum(s => s.Points) + UnfittedMounts.Sum(m => m.Points);

    /// <summary>Everything aboard was fitted, within the points and limits.</summary>
    public bool Certified => UnfittedWeapons.Count == 0 && UnfittedSystems.Count == 0 && UnfittedMounts.Count == 0
                             && Spent <= Budget && OverLimit.Count == 0 && Mounts <= MaxMounts;
}
