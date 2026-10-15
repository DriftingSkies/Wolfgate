using System.Linq;
using System.Numerics;
using Content.Server._Mono.ShipRepair;
using Content.Server._NF.Bank;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Preferences.Managers;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Preferences;
using Content.Shared.Station;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.RoundSetup;

/// <summary>Prepares civilian ship starts and charges their host once through the existing bank.</summary>
public sealed partial class WFShipScenarioSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IMapManager _maps = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private SectorServiceSystem _sectorServices = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private SharedIdCardSystem _cards = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private ShipRepairSystem _repair = default!;
    [Dependency] private WFShipAccessServerSystem _access = default!;

    private readonly Dictionary<int, Scenario> _scenarios = new();

    /// <summary>Holds the original purchase receipt until launch has finished or been refunded.</summary>
    private sealed class Scenario(EntityUid hull, EntityUid station, string vessel)
    {
        public readonly EntityUid Hull = hull;
        public readonly EntityUid Station = station;
        public readonly string Vessel = vessel;
        public readonly HashSet<EntityUid> Grids = new() { hull };
        public ICommonSession? Buyer;
        public int CharacterSlot;
        public int Paid;
        public bool Used;
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridSplitEvent>(OnSplit);
        SubscribeLocalEvent<WFShipScenarioComponent, EntityTerminatingEvent>(OnHullTerminating);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRestart);
    }

    /// <summary>Lists unrestricted civilian designs at the authoritative shipyard price.</summary>
    public WFRoundSetupVessel[] GetVessels() => _prototypes.EnumeratePrototypes<VesselPrototype>()
        .Where(IsEligible).OrderBy(v => v.Price).ThenBy(v => v.Name)
        .Select(v => new WFRoundSetupVessel(v.ID, v.Name, v.Description, v.Price)).ToArray();

    /// <summary>Rejects restricted, voucher-only, abstract and free designs even in forged requests.</summary>
    public bool IsVesselAvailable(string? id) => id != null &&
        _prototypes.TryIndex<VesselPrototype>(id, out var vessel) && IsEligible(vessel);

    private static bool IsEligible(VesselPrototype vessel) => !vessel.Abstract && vessel.Purchasable &&
        vessel.Price > 0 && vessel.Group == ShipyardConsoleUiKey.Shipyard && string.IsNullOrEmpty(vessel.Access);

    /// <summary>Shows only the host's selected character's spendable bank account.</summary>
    public int? GetBalance(ICommonSession host) =>
        _preferences.GetPreferencesOrNull(host.UserId)?.SelectedCharacter is HumanoidCharacterProfile profile &&
        !profile.TraitPreferences.Contains("Ironman") ? profile.BankBalance : null;

    /// <summary>Loads a selected ship on a clear part of the sector before any money is withdrawn.</summary>
    public bool TryPrepare(int lobbyId, string? vesselId, out EntityUid station, out string reason)
    {
        station = EntityUid.Invalid;
        reason = "wf-round-setup-ship-invalid";
        if (!IsVesselAvailable(vesselId))
            return false;
        if (_scenarios.TryGetValue(lobbyId, out var existing))
        {
            if (existing.Vessel == vesselId && !TerminatingOrDeleted(existing.Station) && TryGetSpawn(lobbyId, out _))
            {
                station = existing.Station;
                return true;
            }
            if (existing.Used || existing.Paid > 0)
            {
                reason = "wf-round-setup-ship-gone";
                return false;
            }
            Rollback(lobbyId);
        }
        var mapId = _ticker.DefaultMap;
        if (!_map.TryGetMap(mapId, out var mapEntity))
        {
            reason = "wf-round-setup-ship-no-sector";
            return false;
        }
        var vessel = _prototypes.Index<VesselPrototype>(vesselId!);
        reason = "wf-round-setup-ship-load-failed";
        if (!_loader.TryLoadGrid(mapId, vessel.ShuttlePath, out var loaded, offset: _random.NextVector2(1500f, 6000f)))
            return false;
        var hull = loaded.Value.Owner;
        EntityManager.AddComponents(hull, vessel.AddComponents);
        if (!HasComp<ShuttleComponent>(hull) || !TryFindFloor(hull, out _) || !FindClearPosition(hull, loaded.Value.Comp, mapEntity.Value, mapId))
        {
            Del(hull);
            return false;
        }
        station = _stations.InitializeNewStation(new StationConfig
        {
            StationPrototype = "WFShipScenarioStation",
            StationComponentOverrides = new ComponentRegistry(),
        }, new[] { hull }, vessel.Name);
        EnsureComp<WFShipScenarioStationComponent>(station).LobbyId = lobbyId;
        EnsureComp<WFShipScenarioComponent>(hull).LobbyId = lobbyId;
        _scenarios.Add(lobbyId, new Scenario(hull, station, vessel.ID));
        return true;
    }

    /// <summary>Reserves the selected character's payment once, after validating the prepared ship.</summary>
    public bool TryCharge(int lobbyId, ICommonSession host, out string reason)
    {
        reason = "wf-round-setup-ship-gone";
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || TerminatingOrDeleted(scenario.Station) ||
            TerminatingOrDeleted(scenario.Hull) || !TryGetSpawn(lobbyId, out _) || !IsVesselAvailable(scenario.Vessel))
            return false;
        if (!HasSectorServices())
        {
            reason = "wf-round-setup-ship-no-sector";
            return false;
        }
        var preferences = _preferences.GetPreferencesOrNull(host.UserId);
        reason = "wf-round-setup-ship-bank-unavailable";
        if (preferences?.SelectedCharacter is not HumanoidCharacterProfile profile || profile.TraitPreferences.Contains("Ironman"))
            return false;
        if (scenario.Paid > 0)
            return scenario.Buyer?.UserId == host.UserId && scenario.CharacterSlot == preferences.SelectedCharacterIndex;
        var vessel = _prototypes.Index<VesselPrototype>(scenario.Vessel);
        var attempt = new AttemptShipyardShuttlePurchaseEvent(scenario.Hull, host.AttachedEntity ?? EntityUid.Invalid, vessel);
        RaiseLocalEvent(ref attempt);
        if (attempt.Cancelled)
        {
            reason = attempt.CancelReason;
            return false;
        }
        reason = "wf-round-setup-ship-insufficient";
        if (!_bank.TryBankWithdraw(host, preferences, profile, vessel.Price, out _))
            return false;
        scenario.Buyer = host;
        scenario.CharacterSlot = preferences.SelectedCharacterIndex;
        scenario.Paid = vessel.Price;
        return true;
    }

    /// <summary>Gives the successfully spawned buyer a deed and registers normal ship ownership.</summary>
    public bool CompletePurchase(int lobbyId, ICommonSession host)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || scenario.Paid <= 0 || scenario.Buyer?.UserId != host.UserId)
            return false;
        if (scenario.Used)
            return true;
        if (!HasSectorServices() || host.AttachedEntity is not { } body || !scenario.Grids.Contains(Transform(body).GridUid ?? EntityUid.Invalid) ||
            TerminatingOrDeleted(scenario.Hull) || TerminatingOrDeleted(scenario.Station))
            return false;
        EntityUid card;
        if (_cards.TryFindIdCard(body, out var found))
            card = found.Owner;
        else
        {
            card = Spawn("IDCardStandard", Transform(body).Coordinates);
            _cards.TryChangeFullName(card, Name(body));
            _hands.TryPickupAnyHand(body, card);
        }
        var vessel = _prototypes.Index<VesselPrototype>(scenario.Vessel);
        if (!_shipyard.TryAssignDeed(scenario.Hull, card, host, vessel, existingStation: scenario.Station))
            return false;
        scenario.Used = true;
        EnsureComp<ShipActivityComponent>(scenario.Hull);
        _repair.GenerateRepairData(scenario.Hull);
        return true;
    }

    /// <summary>Admits a spawned crew member's card to the purchased ship's existing access list.</summary>
    public void NotifyArrival(int lobbyId, ICommonSession session)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || !scenario.Used || session.AttachedEntity is not { } body)
            return;
        foreach (var grid in scenario.Grids)
        {
            if (TryComp<WFShipAccessComponent>(grid, out var access))
                _access.TryAddPerson((grid, access), body);
        }
    }

    /// <summary>Returns deck coordinates only on this lobby's surviving ship sections.</summary>
    public bool TryGetSpawn(int lobbyId, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || TerminatingOrDeleted(scenario.Station))
            return false;
        foreach (var grid in scenario.Grids)
        {
            if (!TerminatingOrDeleted(grid) && TryFindFloor(grid, out coordinates))
                return true;
        }
        return false;
    }

    /// <summary>Refunds the original character from fresh preferences, then deletes an unused preparation.</summary>
    public void Rollback(int lobbyId)
    {
        if (!_scenarios.TryGetValue(lobbyId, out var scenario) || scenario.Used)
            return;
        if (scenario.Paid > 0 && scenario.Buyer is { } buyer)
        {
            var preferences = _preferences.GetPreferencesOrNull(buyer.UserId);
            if (preferences == null || !preferences.Characters.TryGetValue(scenario.CharacterSlot, out var current) ||
                current is not HumanoidCharacterProfile profile || !_bank.TryBankDeposit(buyer, preferences, profile, scenario.Paid, out _))
            {
                Log.Error($"Could not refund scenario {lobbyId} purchase for {buyer.UserId} slot {scenario.CharacterSlot}.");
                return;
            }
            scenario.Paid = 0;
        }
        _scenarios.Remove(lobbyId);
        foreach (var grid in scenario.Grids)
        {
            if (!TerminatingOrDeleted(grid))
                Del(grid);
        }
        if (!TerminatingOrDeleted(scenario.Station))
            Del(scenario.Station);
    }

    /// <summary>Closing a used crew leaves its purchased ship and passengers intact.</summary>
    public void RemoveScenario(int lobbyId)
    {
        if (_scenarios.TryGetValue(lobbyId, out var scenario) && scenario.Used)
            _scenarios.Remove(lobbyId);
        else
            Rollback(lobbyId);
    }

    /// <summary>Requires the sector registry before accepting payment or issuing a deed.</summary>
    private bool HasSectorServices()
    {
        var service = _sectorServices.GetServiceEntity();
        return service.Valid && !TerminatingOrDeleted(service);
    }

    /// <summary>Moves a newly loaded hull away from existing grids before admitting its crew.</summary>
    private bool FindClearPosition(EntityUid hull, MapGridComponent grid, EntityUid map, MapId mapId)
    {
        var nearby = new List<Entity<MapGridComponent>>();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var position = _random.NextVector2(1500f, 6000f);
            _transform.SetCoordinates(hull, new EntityCoordinates(map, position));
            var bounds = _transform.GetWorldMatrix(hull).TransformBox(grid.LocalAABB).Enlarged(100f);
            nearby.Clear();
            _maps.FindGridsIntersecting(mapId, bounds, ref nearby, approx: true, includeMap: false);
            if (nearby.All(other => other.Owner == hull))
                return true;
        }
        return false;
    }

    /// <summary>Finds an unobstructed floor without borrowing another ship's spawn points.</summary>
    private bool TryFindFloor(EntityUid hull, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<MapGridComponent>(hull, out var grid) || Transform(hull).MapUid == null)
            return false;
        var tiles = _map.GetAllTilesEnumerator(hull, grid);
        var count = 0;
        while (tiles.MoveNext(out var tile))
        {
            if (tile.Value.Tile.IsEmpty || _turf.IsSpace(tile.Value) || _turf.IsTileBlocked(tile.Value, CollisionGroup.MobMask))
                continue;
            if (_random.Next(++count) == 0)
                coordinates = _turf.GetTileCenter(tile.Value);
        }
        return count > 0;
    }

    /// <summary>Keeps late arrivals confined to fragments of the purchased hull.</summary>
    private void OnSplit(ref GridSplitEvent args)
    {
        if (!TryComp<WFShipScenarioComponent>(args.Grid, out var original) || !_scenarios.TryGetValue(original.LobbyId, out var scenario))
            return;
        foreach (var grid in args.NewGrids)
        {
            EnsureComp<WFShipScenarioComponent>(grid).LobbyId = original.LobbyId;
            scenario.Grids.Add(grid);
        }
    }

    /// <summary>Revokes surviving cards before their networked deed can reference a deleted scenario hull.</summary>
    private void OnHullTerminating(Entity<WFShipScenarioComponent> ent, ref EntityTerminatingEvent args)
    {
        var owners = new List<EntityUid>();
        var deeds = AllEntityQuery<ShuttleDeedComponent>();
        while (deeds.MoveNext(out var owner, out var deed))
        {
            if (deed.ShuttleUid == ent.Owner && !TerminatingOrDeleted(owner))
                owners.Add(owner);
        }
        foreach (var owner in owners)
            RemComp<ShuttleDeedComponent>(owner);
    }
    private void OnRestart(RoundRestartCleanupEvent args)
    {
        foreach (var id in _scenarios.Keys.ToArray())
            RemoveScenario(id);
    }
}
