using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Bank;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._WF.Hideouts.Shipwright;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server._WF.Planets;
using Content.Server._WF.ShipAccess;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._WF.Hideouts;
using Content.Shared._WF.Outposts;
using Content.Shared._WF.Planets;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// Builds a planetside test ground for outposts, hideout modules and the shipwright: a fresh world of its own, the
/// caller standing on it beside a founded outpost with every module built, the shipwright bay, hangar and buildings laid,
/// a stock ship on the hangar pad and money to spend. With <c>gallery</c>, the outpost is laid out as a gallery instead:
/// every layout of every module along paved streets, in each theme in turn, with a stake of each module to try.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class WFHideoutTestCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private Robust.Shared.Configuration.IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private Content.Server.Parallax.BiomeSystem _biome = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShipyardSystem _shipyard = default!;
    [Dependency] private WFHideoutSystem _hideouts = default!;
    [Dependency] private WFLandingPadSystem _pads = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFPlanetNetworkSystem _networks = default!;
    [Dependency] private WFShipAccessServerSystem _shipAccess = default!;
    [Dependency] private WFShipwrightSystem _shipwright = default!;

    public override string Command => "wfhideouttest";

    private const string Surface = "WFSurfaceAsclepiu";
    private const string Kit = "WFOutpostConsoleKit";
    private const string TestShip = "Guppy";
    private const int Money = 50_000_000;

    /// <summary>Where the outpost is founded on the test world, and the patch cleared round it.</summary>
    private static readonly Vector2 Site = new(6.5f, 0.5f);
    private static readonly Vector2i ClearFrom = new(-30, -40);
    private static readonly Vector2i ClearTo = new(42, 28);

    /// <summary>Each building laid on the test outpost: its module and level, where the stake points and the way it faces.</summary>
    private static readonly (string Module, int Level, Vector2 At, Direction Facing)[] Buildings =
    {
        ("WFHideoutShipwright", 1, new Vector2(6.5f, -2.5f), Direction.South),
        ("WFHideoutHangar", 1, new Vector2(6.5f, 3.5f), Direction.North),
        ("WFHideoutQuarters", 1, new Vector2(18.5f, 6.5f), Direction.East),
        ("WFHideoutWorkshop", 1, new Vector2(-6.5f, 4.5f), Direction.West),
    };

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1 ? CompletionResult.FromOptions(new[] { GalleryArg }) : CompletionResult.Empty;
    }

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var gallery = args.Length > 0 && args[0] == GalleryArg;
        if (args.Length > (gallery ? 1 : 0))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-help"));
            return;
        }

        if (shell.Player?.AttachedEntity is not { } user)
        {
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-no-body"));
            return;
        }

        if (_outposts.TryGetOutpost(shell.Player.UserId, out _))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-has-outpost"));
            return;
        }

        // A test world needs planet networks; a server built without the development preset has them off.
        if (!_cfg.GetCVar(Content.Shared._WF.CCVar.PlanetCVars.PlanetNetworks))
        {
            _cfg.SetCVar(Content.Shared._WF.CCVar.PlanetCVars.PlanetNetworks, true);
            shell.WriteLine(Loc.GetString("cmd-wfhideouttest-networks-on"));
        }

        if (!_proto.TryIndex<WFPlanetSurfacePrototype>(Surface, out var surface))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-no-surface", ("surface", Surface)));
            return;
        }

        if (_networks.BuildNetwork(surface, new Vector2(0f, 0f), Loc.GetString("cmd-wfhideouttest-world"), null) is not { } network
            || !EntityManager.TryGetComponent<WFPlanetNetworkComponent>(network, out var comp))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-world-failed"));
            return;
        }

        var ground = comp.GroundMap;
        _transform.SetCoordinates(user, new EntityCoordinates(ground, Site + new Vector2(0f, -1.5f)));
        shell.WriteLine(Loc.GetString("cmd-wfhideouttest-started"));
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{shell.Player.Name} built a hideout test world");

        // The world lays its outpost plots as it preloads; the outpost goes up once they are there.
        // Without a preload the world never lays its plots, so there is nothing to wait for.
        WaitForPlots(shell, user, ground, _cfg.GetCVar(Content.Shared._WF.CCVar.PlanetCVars.Preload) ? 30 : 1, gallery);
    }

    /// <summary>Waits for the world to lay its outpost plots, up to <paramref name="tries"/> checks two seconds apart, then builds.</summary>
    private void WaitForPlots(IConsoleShell shell, EntityUid user, EntityUid ground, int tries, bool gallery)
    {
        Timer.Spawn(TimeSpan.FromSeconds(2), () =>
        {
            if (EntityManager.Deleted(ground))
                return;

            var ready = EntityManager.TryGetComponent<Content.Server._WF.PlanetPois.WFPlanetPoiComponent>(ground, out var pois)
                        && pois.OutpostLots.Count > 0;
            if (!ready && tries > 1)
            {
                WaitForPlots(shell, user, ground, tries - 1, gallery);
                return;
            }

            Build(shell, user, ground, gallery);
        });
    }

    private void Build(IConsoleShell shell, EntityUid user, EntityUid ground, bool gallery)
    {
        if (EntityManager.Deleted(user) || EntityManager.Deleted(ground) || !EntityManager.TryGetComponent<MapGridComponent>(ground, out var grid))
            return;

        // On one of the world's outpost plots, past its sites and roads, as the registry at Central founds one; a world
        // built without its plots (preload off) takes the old spot by the caller instead.
        string reason;
        var site = Site;
        EntityUid? founded;
        if (!_outposts.TryFoundOnPlot(shell.Player!.UserId, EntityManager.GetComponent<MetaDataComponent>(user).EntityName, ground, user,
                out founded, out reason))
        {
            ClearPatch(ground, grid, Vector2i.Zero, gallery);
            var kit = EntityManager.SpawnEntity(Kit, new EntityCoordinates(ground, Site));
            if (!_outposts.TryFound(user, (kit, EntityManager.GetComponent<WFOutpostKitComponent>(kit)), new EntityCoordinates(ground, Site),
                    out founded, out reason))
            {
                shell.WriteError(Loc.GetString("cmd-wfhideouttest-found-failed", ("reason", Loc.GetString(reason))));
                return;
            }
        }
        else
        {
            // Buildings go relative to the outpost's console, wherever its plot is.
            var console = EntityManager.GetComponent<WFOutpostComponent>(founded.Value).Console;
            site = _transform.ToMapCoordinates(EntityManager.GetComponent<TransformComponent>(console).Coordinates).Position;
            var local = Vector2.Transform(site, _transform.GetInvWorldMatrix(ground));
            ClearPatch(ground, grid, new Vector2i((int) MathF.Floor(local.X - Site.X), (int) MathF.Floor(local.Y - Site.Y)), gallery);
            _transform.SetCoordinates(user, new EntityCoordinates(ground, local + new Vector2(0f, -1.5f)));
            site = local;
        }

        var outpost = new Entity<WFOutpostComponent>(founded.Value, EntityManager.GetComponent<WFOutpostComponent>(founded.Value));
        _bank.TryBankDeposit(user, Money, tax: false);
        _hideouts.LeaveTradeHub(outpost);

        // Every module at its top level, with nothing handed out, then each building laid where it goes.
        foreach (var module in _proto.EnumeratePrototypes<WFHideoutModulePrototype>())
        {
            if (!module.Company)
                _hideouts.SetLevel(outpost, module.ID, module.Levels.Count);
        }

        if (gallery)
        {
            LayGallery(shell, user, outpost, ground, site);
            return;
        }

        foreach (var (module, level, at, facing) in Buildings)
        {
            if (!_proto.TryIndex<WFHideoutModulePrototype>(module, out var proto) || proto.Levels[level - 1].Prefab is not { } prefab)
                continue;

            if (!_outposts.TryStampPrefab(user, outpost, prefab, new EntityCoordinates(ground, at - Site + site), facing, out reason))
                shell.WriteError(Loc.GetString("cmd-wfhideouttest-prefab-failed", ("module", module), ("reason", Loc.GetString(reason))));
        }

        // Laying refreshes the pads' widths and links the bay's cranes.
        _hideouts.SetLevel(outpost, "WFHideoutShipwright", 3);
        var consoles = EntityManager.EntityQueryEnumerator<WFShipwrightConsoleComponent, TransformComponent>();
        while (consoles.MoveNext(out var console, out var consoleComp, out var xform))
        {
            if (xform.GridUid == outpost.Owner)
                _shipwright.LinkCranes((console, consoleComp));
        }

        ParkTestShip(user, outpost, shell);
        shell.WriteLine(Loc.GetString("cmd-wfhideouttest-done", ("money", Money)));
    }

    /// <summary>
    /// Grows the ground round the site and clears rock and growth off it so every building has room. The ground keeps its own tiles:
    /// an outpost only grows onto natural ground, so laying floor here would block every plate and module.
    /// </summary>
    private void ClearPatch(EntityUid ground, MapGridComponent grid, Vector2i shift, bool gallery)
    {
        var clear = new List<EntityUid>();
        var from = gallery ? GalleryFrom : ClearFrom;
        var to = gallery ? GalleryTo : ClearTo;

        // A world only grows its ground near players; the patch may reach past that, and nothing is laid on ground not yet there.
        _biome.ReserveTiles(ground, new Box2(from + shift, to + shift + Vector2i.One), new List<(Vector2i Index, Tile Tile)>());
        for (var x = from.X; x <= to.X; x++)
        for (var y = from.Y; y <= to.Y; y++)
        {
            var index = new Vector2i(x, y) + shift;
            var anchored = _maps.GetAnchoredEntitiesEnumerator(ground, grid, index);
            while (anchored.MoveNext(out var ent))
                clear.Add(ent.Value);
        }

        foreach (var ent in clear)
            EntityManager.DeleteEntity(ent);
    }

    private const string GalleryArg = "gallery";
    private const string StreetTile = "WFFloorColonyGreyBolts";

    /// <summary>The patch cleared for the gallery, round the outpost's site.</summary>
    private static readonly Vector2i GalleryFrom = new(-34, -36);
    private static readonly Vector2i GalleryTo = new(46, 42);

    /// <summary>How far the streets run either side of the console, and the rows' fronts north and south of it.</summary>
    private const int StreetReach = 38;
    private static readonly int[] RowFronts = { 4, 17, 30, -13 };
    private static readonly int[] RowColumns = { -30, -15, 0, 15, 30 };
    private const int WideRow = -26;

    /// <summary>The modules the gallery leaves out: their aprons are as large as the rest of it together.</summary>
    private static readonly string[] GallerySkips = { "WFHideoutHangar", "WFHideoutShipwright" };

    /// <summary>
    /// Lays the outpost out as a gallery: streets round the console, then every layout of every module facing north along
    /// them, each in the next theme, and a stake of each module at the caller's feet.
    /// </summary>
    private void LayGallery(IConsoleShell shell, EntityUid user, Entity<WFOutpostComponent> outpost, EntityUid ground, Vector2 site)
    {
        if (!EntityManager.TryGetComponent<MapGridComponent>(outpost, out var grid))
            return;

        // Streets first, outward from the console, so the claim reaches every row before its buildings land.
        var centre = _maps.LocalToTile(outpost, grid, EntityManager.GetComponent<TransformComponent>(outpost.Comp.Console).Coordinates);
        var streets = new List<(Vector2i, string)>();
        void Street(int x, int y) => streets.Add((centre + new Vector2i(x, y), StreetTile));
        var cross = RowFronts.Append(WideRow).Select(front => front - 1).ToList();
        Street(0, 2);
        for (var x = 0; x <= StreetReach; x++)
        {
            Street(x, 3);
            Street(-x, 3);
        }

        foreach (var side in new[] { -StreetReach, StreetReach })
        {
            for (var y = 4; y <= cross.Max(); y++)
                Street(side, y);
            for (var y = 2; y >= cross.Min(); y--)
                Street(side, y);
        }

        foreach (var y in cross.Where(y => y != 3))
        {
            for (var x = StreetReach - 1; x >= 0; x--)
            {
                Street(x, y);
                Street(-x, y);
            }
        }

        _outposts.PaintFloor(user, outpost, streets, out var refused);
        if (refused != null)
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-street-failed", ("reason", Loc.GetString(refused))));

        // The rows' slots, nearest the console first; the company wing is wide and gets a row of its own.
        var slots = new Queue<Vector2i>(RowFronts.SelectMany(front => RowColumns.Select(column => new Vector2i(column, front)))
            .Append(new Vector2i(25, WideRow)));
        var wide = new Queue<Vector2i>(new[] { new Vector2i(-25, WideRow), new Vector2i(0, WideRow) });
        var themes = _proto.EnumeratePrototypes<WFHideoutThemePrototype>().OrderBy(theme => theme.Order).ThenBy(theme => theme.ID).ToList();
        var feet = EntityManager.GetComponent<TransformComponent>(user).Coordinates;
        var laid = 0;
        var index = 0;
        foreach (var module in _proto.EnumeratePrototypes<WFHideoutModulePrototype>().OrderBy(module => module.Order).ThenBy(module => module.ID))
        {
            if (module.Levels.FirstOrDefault(level => level.Prefab != null) is not { } level)
                continue;

            _hideouts.SpawnStake(module, level, feet);
            if (GallerySkips.Contains(module.ID))
                continue;

            var prefabs = level.Layouts.Count > 0 ? level.Layouts.Select(layout => layout.Prefab).ToList() : new List<ResPath> { level.Prefab!.Value };
            foreach (var prefab in prefabs)
            {
                var from = module.Company ? wide : slots;
                if (!from.TryDequeue(out var slot))
                    break;

                var theme = themes.Count > 0 ? themes[index++ % themes.Count] : null;
                if (_outposts.TryStampPrefab(user, outpost, prefab, new EntityCoordinates(ground, site + (Vector2) slot), Direction.North,
                        out var reason, skin: _hideouts.Skin(theme)))
                    laid++;
                else
                    shell.WriteError(Loc.GetString("cmd-wfhideouttest-prefab-failed", ("module", prefab.ToString()), ("reason", Loc.GetString(reason))));
            }
        }

        shell.WriteLine(Loc.GetString("cmd-wfhideouttest-gallery-done", ("count", laid), ("money", Money)));
    }

    /// <summary>Sets a stock ship of the caller's on the faction hangar's pad, ready to register.</summary>
    private void ParkTestShip(EntityUid user, Entity<WFOutpostComponent> outpost, IConsoleShell shell)
    {
        if (!_hideouts.TryGetHangar(outpost, out var pad, shipwright: false)
            || !_proto.TryIndex<Content.Shared._NF.Shipyard.Prototypes.VesselPrototype>(TestShip, out var vessel)
            || EntityManager.GetComponent<TransformComponent>(outpost).MapUid is not { } map)
            return;

        string yaml;
        try
        {
            yaml = _resources.ContentFileReadAllText(vessel.ShuttlePath);
        }
        catch (IOException)
        {
            return;
        }

        if (!_shipyard.TryAddSavedShip(yaml, out var hull) || !EntityManager.TryGetComponent<MapGridComponent>(hull, out var grid))
            return;

        if (!_pads.TryPlace(hull.Value, grid, pad, outpost, out var placement, out var reason, needPower: false))
        {
            EntityManager.DeleteEntity(hull.Value);
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-ship-failed", ("reason", reason)));
            return;
        }

        _transform.SetCoordinates(hull.Value, new EntityCoordinates(map, placement.Value.Position));
        _transform.SetWorldRotation(hull.Value, placement.Value.Rotation);
        EntityManager.EnsureComponent<VesselComponent>(hull.Value).VesselId = TestShip;
        _shipAccess.SetupRegisteredShip(hull.Value, EntityManager.GetComponent<MetaDataComponent>(user).EntityName, new[] { outpost.Comp.Owner });
        _pads.Park(hull.Value, pad);
    }
}
