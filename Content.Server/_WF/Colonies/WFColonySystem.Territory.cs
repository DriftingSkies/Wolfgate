using System.Linq;
using Content.Server._WF.Defcon;
using Content.Server._WF.SectorControl;
using Content.Server._WF.SectorControl.Systems;
using Content.Shared._WF.Colonies;
using Content.Shared._WF.SectorControl;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Colonies;

/// <summary>
/// The Colonies driver of sector control: a Colony world's cell shows the neutral colonised pattern, a Charter world's its
/// governor's colours. Someone home on the world keeps the cell active; a parked ship can't retake it; the ADS leaves it
/// alone. A blockading faction next door may take it while nobody is home, which blockades the colony.
/// </summary>
public sealed partial class WFColonySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFSectorActivitySystem _activity = default!;
    [Dependency] private WFSectorSyncSystem _sync = default!;
    [Dependency] private WFSectorTerritorySystem _territory = default!;

    /// <summary>The neutral faction colonies hold their cells as below Charter tier.</summary>
    public static readonly ProtoId<WFSectorFactionPrototype> ColonyFaction = "WFSectorFactionColonies";

    private const string CompanyFactionPrefix = "WFColonyCompany";

    private void InitializeTerritory()
    {
        SubscribeLocalEvent<WFSectorRetakeAttemptEvent>(OnRetakeAttempt);
        SubscribeLocalEvent<WFColonyCellQueryEvent>(OnCellQuery);
        SubscribeLocalEvent<WFDefconGatherEvent>(OnDefconGather);
    }

    /// <summary>Whether a faction is one the Colonies driver holds cells as.</summary>
    public static bool IsColonyFaction(string faction) => faction == ColonyFaction || faction.StartsWith(CompanyFactionPrefix);

    /// <summary>The faction a world's cell is held as: its governor's, or the neutral colonies'.</summary>
    private ProtoId<WFSectorFactionPrototype> FactionFor(WorldState world)
    {
        if (world.Tier < WFWorldTier.Charter || world.Charter is not { } charter || _companies.CompanyIdentity(charter.CompanyId) is not { } identity)
            return ColonyFaction;

        var id = CompanyFactionPrefix + charter.CompanyId;
        _territory.RegisterFaction(id, Loc.GetString("wf-colony-faction-company", ("company", identity.Name)), identity.Colour);
        return id;
    }

    /// <summary>The sector cell a world's colony sits in, when its body is on the sector map.</summary>
    private bool TryColonyCell(EntityUid network, out MapId map, out WFSectorCell cell)
    {
        map = _territory.SectorMap;
        cell = default;
        var body = Comp<Planets.WFPlanetNetworkComponent>(network).Planet;
        if (!_territory.Enabled || body is not { } planet || TerminatingOrDeleted(planet) || Transform(planet).MapID != map)
            return false;

        cell = _territory.CellOf(_transform.GetMapCoordinates(planet));
        return true;
    }

    /// <summary>Claims, recolours or gives up each world's cell, and keeps it active while someone is home.</summary>
    private void UpdateTerritory(List<OutpostEntry> entries)
    {
        var lines = new List<string>();
        foreach (var (id, network) in Networks())
        {
            var world = World(id);
            var here = entries.Where(entry => entry.World == id).ToList();
            UpdateLandmarks(network, here);
            if (!TryColonyCell(network, out var map, out var cell))
                continue;

            var owner = _territory.Owner(map, cell);
            var ours = owner is { } held && IsColonyFaction(held);
            if (world.Tier < WFWorldTier.Colony || here.Count == 0)
            {
                if (ours)
                    _territory.Free(map, cell);
                continue;
            }

            lines.Add(Loc.GetString("wf-colony-legend-world", ("world", WorldName(id)),
                ("tier", Loc.GetString($"wf-colony-tier-{world.Tier.ToString().ToLowerInvariant()}"))));
            var home = here.Any(entry => entry.Home);
            if (home)
                _activity.Stamp(map, cell);

            // A blockading faction holding a neighbouring cell may take the colony's while nobody is home to hold it.
            if (ours && !home && BlockadingNeighbour(map, cell))
            {
                _territory.Free(map, cell);
                continue;
            }

            var faction = FactionFor(world);
            if (owner == faction || _territory.ContestOf(map, cell) != null)
                continue;
            if (ours)
                _territory.Free(map, cell);
            if (owner == null || ours)
                _territory.TryClaim(map, cell, faction, null);
        }

        _sync.SetStatus(ColonyFaction, lines.Count == 0 ? null : string.Join(", ", lines));
    }

    /// <summary>Recolours a world's cell at once when its tier or charter changes.</summary>
    private void RefreshTerritory(WorldState world)
    {
        RefreshConsoles(world.Id);
    }

    private bool BlockadingNeighbour(MapId map, WFSectorCell cell)
    {
        foreach (var neighbour in WFSectorHex.Ring(cell, 1))
        {
            if (_territory.Owner(map, neighbour) is { } owner && _proto.TryIndex(owner, out var faction) && faction.Blockades)
                return true;
        }

        return false;
    }

    /// <summary>A parked ship can't retake a colony's cell: only its world's development holds it.</summary>
    private void OnRetakeAttempt(ref WFSectorRetakeAttemptEvent args)
    {
        if (IsColonyFaction(args.Faction))
            args.Cancelled = true;
    }

    private void OnCellQuery(ref WFColonyCellQueryEvent args)
    {
        if (args.Colony)
            return;

        foreach (var (id, network) in Networks())
        {
            if (Tier(id) >= WFWorldTier.Colony && TryColonyCell(network, out var map, out var cell) && map == args.Map && cell == args.Cell)
            {
                args.Colony = true;
                return;
            }
        }
    }

    /// <summary>Every blockaded colony adds to the sector's threat.</summary>
    private void OnDefconGather(WFDefconGatherEvent args)
    {
        var query = EntityQueryEnumerator<WFColonyComponent>();
        while (query.MoveNext(out var uid, out var colony))
        {
            if (colony.Blockaded)
                args.Add(_cfg.GetCVar(ColonyCVars.BlockadeDefcon), Loc.GetString("wf-colony-defcon-blockade", ("world", Name(uid))));
        }
    }
}

/// <summary>Raised broadcast to ask whether a sector cell holds a colony world, which the ADS leaves alone like a station.</summary>
[ByRefEvent]
public record struct WFColonyCellQueryEvent(MapId Map, WFSectorCell Cell)
{
    public bool Colony;
}
