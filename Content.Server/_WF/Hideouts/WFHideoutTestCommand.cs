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
/// a stock ship on the hangar pad and money to spend.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class WFHideoutTestCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private BankSystem _bank = default!;
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
        ("WFHideoutQuarters", 1, new Vector2(18.5f, 4.5f), Direction.East),
        ("WFHideoutWorkshop", 1, new Vector2(-6.5f, 4.5f), Direction.West),
    };

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
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

        if (!_proto.TryIndex<WFPlanetSurfacePrototype>(Surface, out var surface)
            || _networks.BuildNetwork(surface, new Vector2(0f, 0f), Loc.GetString("cmd-wfhideouttest-world"), null) is not { } network
            || !EntityManager.TryGetComponent<WFPlanetNetworkComponent>(network, out var comp))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-world-failed"));
            return;
        }

        var ground = comp.GroundMap;
        _transform.SetCoordinates(user, new EntityCoordinates(ground, Site + new Vector2(0f, -1.5f)));
        shell.WriteLine(Loc.GetString("cmd-wfhideouttest-started"));
        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{shell.Player.Name} built a hideout test world");

        // The terrain streams in round the caller first; the patch is cleared once it has.
        Timer.Spawn(TimeSpan.FromSeconds(4), () => Build(shell, user, ground));
    }

    private void Build(IConsoleShell shell, EntityUid user, EntityUid ground)
    {
        if (EntityManager.Deleted(user) || EntityManager.Deleted(ground) || !EntityManager.TryGetComponent<MapGridComponent>(ground, out var grid))
            return;

        ClearPatch(ground, grid);
        var kit = EntityManager.SpawnEntity(Kit, new EntityCoordinates(ground, Site));
        if (!_outposts.TryFound(user, (kit, EntityManager.GetComponent<WFOutpostKitComponent>(kit)), new EntityCoordinates(ground, Site),
                out var founded, out var reason))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideouttest-found-failed", ("reason", Loc.GetString(reason))));
            return;
        }

        var outpost = new Entity<WFOutpostComponent>(founded.Value, EntityManager.GetComponent<WFOutpostComponent>(founded.Value));
        _bank.TryBankDeposit(user, Money, tax: false);

        // Every module at its top level, with nothing handed out, then each building laid where it goes.
        foreach (var module in _proto.EnumeratePrototypes<WFHideoutModulePrototype>())
        {
            if (!module.Company)
                _hideouts.SetLevel(outpost, module.ID, module.Levels.Count);
        }

        foreach (var (module, level, at, facing) in Buildings)
        {
            if (!_proto.TryIndex<WFHideoutModulePrototype>(module, out var proto) || proto.Levels[level - 1].Prefab is not { } prefab)
                continue;

            if (!_outposts.TryStampPrefab(user, outpost, prefab, new EntityCoordinates(ground, at), facing, out reason))
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
    /// Clears rock and growth off the ground round the site so every building has room. The ground keeps its own tiles:
    /// an outpost only grows onto natural ground, so laying floor here would block every plate and module.
    /// </summary>
    private void ClearPatch(EntityUid ground, MapGridComponent grid)
    {
        var clear = new List<EntityUid>();
        for (var x = ClearFrom.X; x <= ClearTo.X; x++)
        for (var y = ClearFrom.Y; y <= ClearTo.Y; y++)
        {
            var index = new Vector2i(x, y);
            var anchored = _maps.GetAnchoredEntitiesEnumerator(ground, grid, index);
            while (anchored.MoveNext(out var ent))
                clear.Add(ent.Value);
        }

        foreach (var ent in clear)
            EntityManager.DeleteEntity(ent);
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
