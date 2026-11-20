using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.Station.Components;
using Content.Server.StationEvents.Components;
using Content.Shared._WG.ShipAdmin;
using Content.Shared.Database;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipAdmin;

public sealed partial class WGShipAdminSystem
{
    [Dependency] private GameTicker _ticker = default!;

    /// <summary>
    /// Events that happen aboard a station and pick it, or a tile on it, through the game rule pickers that honour
    /// <see cref="WGEventTargetComponent"/>. Sector-wide events and those that pick their target another way are left out.
    /// </summary>
    private static readonly string[] AboardEvents =
    {
        "GasLeak",
        "VentClog",
        "MouseMigration",
        "KingRatMigration",
        "CockroachMigration",
        "SnailMigration",
        "RandomSentience",
        "ImmovableRodSpawn",
        "BluespaceCargoCrate",
    };

    private List<WGShipAdminEvent>? _events;

    /// <summary>
    /// The events the panel offers: those that happen aboard, and every station event that loads ships (raiders and
    /// strangers), which arrive beside the target.
    /// </summary>
    public List<WGShipAdminEvent> ListEvents()
    {
        if (_events != null)
            return _events;

        _events = new List<WGShipAdminEvent>();
        foreach (var id in AboardEvents)
        {
            if (_proto.TryIndex<EntityPrototype>(id, out var proto))
                _events.Add(new WGShipAdminEvent { Id = id, Name = EventName(proto) });
        }

        var factory = EntityManager.ComponentFactory;
        foreach (var proto in _proto.EnumeratePrototypes<EntityPrototype>()
                     .Where(proto => !proto.Abstract
                                     && proto.TryGetComponent<LoadMapRuleComponent>(out _, factory)
                                     && proto.TryGetComponent<StationEventComponent>(out _, factory))
                     .OrderBy(proto => proto.ID))
        {
            _events.Add(new WGShipAdminEvent { Id = proto.ID, Name = EventName(proto), Arrival = true });
        }

        return _events;
    }

    /// <summary>
    /// Starts an event aimed at a ship. One that happens aboard picks the ship's station and a tile on the ship, or
    /// fails without falling back to another station; one that brings ships puts them beside it.
    /// </summary>
    public bool StartEvent(ICommonSession admin, EntityUid grid, string rule, out string result)
    {
        var choice = ListEvents().FirstOrDefault(e => e.Id == rule);
        if (choice == null)
        {
            result = Loc.GetString("wg-ship-admin-result-bad-event");
            return false;
        }

        // Checked before the rule is added, so a refused event leaves nothing behind.
        if (Transform(grid).MapUid == null)
        {
            result = Loc.GetString("wg-ship-admin-result-event-no-map", ("ship", Name(grid)));
            return false;
        }

        var station = _station.GetOwningStation(grid);
        if (!choice.Arrival && station == null)
        {
            result = Loc.GetString("wg-ship-admin-result-event-no-station", ("ship", Name(grid)));
            return false;
        }

        // Aboard events pick by station, and several of them (vents, critters, sentience) then reach every grid the
        // station has. A ship's station is the ship alone; one with more grids would spill onto the others.
        if (!choice.Arrival && CompOrNull<StationDataComponent>(station)?.Grids.Count > 1)
        {
            result = Loc.GetString("wg-ship-admin-result-event-shared-station", ("ship", Name(grid)), ("station", Name(station!.Value)));
            return false;
        }

        var ruleEntity = _ticker.AddGameRule(rule);
        int arrived = 0, loaded = 0;
        WGEventTargetComponent? target = null;
        if (choice.Arrival)
        {
            // The ships load when the rule is added; bring each beside the target before the rule starts.
            if (TryComp<RuleGridsComponent>(ruleEntity, out var grids))
            {
                foreach (var ship in grids.MapGrids)
                {
                    if (!HasComp<MapGridComponent>(ship))
                        continue;

                    loaded++;
                    if (_shuttle.TryFTLProximity(ship, grid))
                        arrived++;
                }
            }
        }
        else
        {
            target = EnsureComp<WGEventTargetComponent>(ruleEntity);
            target.Station = station!.Value;
            target.Grid = grid;
            target.Admin = admin;
        }

        // A rule whose ships failed to load has ended already, and an ended rule doesn't start.
        if (!_ticker.StartGameRule(ruleEntity))
        {
            Audit(admin, LogImpact.Medium, grid, $"tried to start the event {rule} aimed at it, which refused to start");
            result = Loc.GetString("wg-ship-admin-result-event-refused", ("event", choice.Name));
            return false;
        }

        var queued = HasComp<DelayedStartRuleComponent>(ruleEntity);
        Audit(admin, LogImpact.High, grid, $"{(queued ? "queued" : "started")} the event {rule} aimed at it");

        // An event that started at once and found nothing aboard has been reported to the panel already.
        if (target is { Missed: true })
        {
            result = Loc.GetString("wg-ship-admin-result-event-missed", ("event", choice.Name), ("ship", Name(grid)));
            return false;
        }

        if (choice.Arrival)
        {
            result = Loc.GetString(arrived == loaded ? "wg-ship-admin-result-event-arrival" : "wg-ship-admin-result-event-arrival-partial",
                ("event", choice.Name), ("ship", Name(grid)), ("arrived", arrived), ("loaded", loaded));
            return arrived == loaded;
        }

        result = Loc.GetString(queued ? "wg-ship-admin-result-event-queued" : "wg-ship-admin-result-event",
            ("event", choice.Name), ("ship", Name(grid)));
        return true;
    }

    /// <summary>Tells the admin who aimed an event that it found nothing it could use aboard, once it tried.</summary>
    private void OnEventTargetMissed(Entity<WGEventTargetComponent> rule, ref WGEventTargetMissedEvent args)
    {
        var name = MetaData(rule).EntityPrototype?.ID ?? Name(rule);
        if (rule.Comp.Admin is { } admin)
            Audit(admin, LogImpact.Medium, rule.Comp.Grid, $"the event {name} aimed at it found no target aboard");

        if (rule.Comp.Admin is { } told && _panels.TryGetValue(told, out var panel))
        {
            var ship = TerminatingOrDeleted(rule.Comp.Grid) ? Loc.GetString("wg-ship-admin-gone-ship") : Name(rule.Comp.Grid);
            panel.SetResult(Loc.GetString("wg-ship-admin-result-event-missed", ("event", name), ("ship", ship)), true);
        }
    }

    private static string EventName(EntityPrototype proto)
    {
        return string.IsNullOrWhiteSpace(proto.Name) ? proto.ID : $"{proto.Name} ({proto.ID})";
    }
}
