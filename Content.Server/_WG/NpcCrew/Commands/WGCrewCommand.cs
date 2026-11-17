using System.Linq;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Administration;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._WG.NpcCrew.Commands;

/// <summary>
/// NPC crew from the console: plan a grid, spawn the plan or one role, list, clear a group, change a duty, give a
/// pilot orders, set a radio officer's callsign, set up a test arena.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class WGCrewCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewPlannerSystem _planner = default!;
    [Dependency] private WGPilotDutySystem _pilot = default!;
    [Dependency] private WGRadioOperatorSystem _radio = default!;

    private static readonly string[] Subcommands = { "plan", "spawn", "spawnrole", "list", "clear", "duty", "orders", "callsign", "arena" };

    private static readonly string[] OrderNames = { "hold", "goto", "loiter", "follow", "dock", "undock" };

    public override string Command => "wg_crew";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case "plan":
                Plan(shell, args);
                break;
            case "spawn":
                SpawnPlan(shell, args);
                break;
            case "spawnrole":
                SpawnRole(shell, args);
                break;
            case "list":
                List(shell, args);
                break;
            case "clear":
                Clear(shell, args);
                break;
            case "duty":
                Duty(shell, args);
                break;
            case "orders":
                Orders(shell, args);
                break;
            case "callsign":
                Callsign(shell, args);
                break;
            case "arena":
                Arena(shell, args);
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-wg_crew-unknown", ("sub", args[0])));
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wg_crew-hint-sub")),
            2 when args[0] == "spawnrole" => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<WGCrewRolePrototype>().Select(role => role.ID).Order(),
                Loc.GetString("cmd-wg_crew-hint-role")),
            2 when args[0] == "arena" => CompletionResult.FromHintOptions(VesselIds().Prepend("clear"), Loc.GetString("cmd-wg_crew-hint-vessel")),
            2 when args[0] is "plan" or "spawn" => CompletionResult.FromHint(Loc.GetString("cmd-wg_crew-hint-grid")),
            2 when args[0] is "duty" or "orders" or "callsign" => CompletionResult.FromHint(Loc.GetString("cmd-wg_crew-hint-mob")),
            3 when args[0] == "orders" => CompletionResult.FromHintOptions(OrderNames, Loc.GetString("cmd-wg_crew-hint-order")),
            4 when args[0] == "orders" && args[2] is "follow" or "dock" =>
                CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wg_crew-hint-grid")),
            >= 3 when args[0] == "callsign" => CompletionResult.FromHint(Loc.GetString("cmd-wg_crew-hint-callsign")),
            _ => CompletionResult.Empty,
        };
    }

    /// <summary>wg_crew plan &lt;grid|here&gt; [deckhands]</summary>
    private void Plan(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryGrid(shell, args[1], out var grid) || !TryCount(shell, args, 2, out var deckhands))
            return;

        var plan = _planner.Plan(grid, deckhands);
        shell.WriteLine(Loc.GetString("cmd-wg_crew-plan-header",
            ("count", plan.Count),
            ("grid", EntityManager.ToPrettyString(grid))));
        foreach (var post in plan)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_crew-plan-line",
                ("kind", Loc.GetString($"wg-crew-post-{post.Kind.ToString().ToLowerInvariant()}")),
                ("role", post.Role.Id),
                ("x", post.Coordinates.X.ToString("0.0")),
                ("y", post.Coordinates.Y.ToString("0.0"))));
        }
    }

    /// <summary>wg_crew spawn &lt;grid|here&gt; [group] [deckhands]</summary>
    private void SpawnPlan(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryGrid(shell, args[1], out var grid) || !TryCount(shell, args, 3, out var deckhands))
            return;

        var group = args.Length >= 3 ? args[2] : DefaultGroup(grid);
        var crew = _crew.SpawnCrew(_planner.Plan(grid, deckhands), group);
        shell.WriteLine(Loc.GetString("cmd-wg_crew-spawned", ("count", crew.Count), ("group", group)));
    }

    /// <summary>wg_crew spawnrole &lt;role&gt; [group]: at the caller, with the post there.</summary>
    private void SpawnRole(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player)
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-no-player"));
            return;
        }

        if (!TryRole(args[1], out var role))
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-unknown-role", ("role", args[1])));
            return;
        }

        var xform = EntityManager.GetComponent<TransformComponent>(player);
        var group = args.Length == 3 ? args[2] : xform.GridUid is { } grid ? DefaultGroup(grid) : "crew";
        if (_crew.SpawnCrewman(role.ID, xform.Coordinates, group) is not { } uid)
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-spawn-failed", ("role", role.ID)));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_crew-spawned-one",
            ("name", EntityManager.GetComponent<MetaDataComponent>(uid).EntityName),
            ("uid", uid),
            ("group", group)));
    }

    /// <summary>wg_crew list [group]</summary>
    private void List(IConsoleShell shell, string[] args)
    {
        var count = 0;
        var query = EntityManager.EntityQueryEnumerator<WGCrewComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var crew, out var meta))
        {
            if (args.Length >= 2 && crew.Group != args[1])
                continue;

            count++;
            shell.WriteLine(Loc.GetString("cmd-wg_crew-list-line",
                ("uid", uid),
                ("name", meta.EntityName),
                ("role", crew.Role?.Id ?? "-"),
                ("duty", crew.Duty),
                ("group", crew.Group)));
        }

        shell.WriteLine(Loc.GetString("cmd-wg_crew-list-footer", ("count", count)));
    }

    /// <summary>wg_crew clear &lt;group&gt;</summary>
    private void Clear(IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteLine(Help);
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_crew-cleared", ("count", _setup.ClearGroup(args[1])), ("group", args[1])));
    }

    /// <summary>wg_crew duty &lt;mob&gt; &lt;duty&gt;</summary>
    private void Duty(IConsoleShell shell, string[] args)
    {
        if (args.Length != 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.TryGetComponent<WGCrewComponent>(uid, out var crew))
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-not-crew", ("arg", args[1])));
            return;
        }

        _crew.SetDuty((uid.Value, crew), args[2]);
        shell.WriteLine(Loc.GetString("cmd-wg_crew-duty-set",
            ("name", EntityManager.GetComponent<MetaDataComponent>(uid.Value).EntityName),
            ("duty", args[2])));
    }

    /// <summary>
    /// wg_crew orders &lt;mob&gt; hold | goto &lt;x&gt; &lt;y&gt; [&lt;x&gt; &lt;y&gt; ...] | loiter &lt;x&gt; &lt;y&gt; &lt;radius&gt; |
    /// follow &lt;grid|here&gt; | dock &lt;grid|here&gt; | undock. Coordinates are map coordinates on the pilot's map.
    /// </summary>
    private void Orders(IConsoleShell shell, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net)
            || !EntityManager.TryGetEntity(net, out var found)
            || !EntityManager.TryGetComponent<WGPilotDutyComponent>(found, out var duty))
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-not-pilot", ("arg", args[1])));
            return;
        }

        var uid = found.Value;
        var name = EntityManager.GetComponent<MetaDataComponent>(uid).EntityName;
        if (EntityManager.GetComponent<TransformComponent>(uid).MapUid is not { } map)
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-not-on-map", ("name", name)));
            return;
        }

        switch (args[2])
        {
            case "hold" when args.Length == 3:
                _pilot.Hold((uid, duty));
                break;
            case "goto" when args.Length >= 5 && (args.Length - 3) % 2 == 0:
                var waypoints = new List<EntityCoordinates>();
                for (var i = 3; i < args.Length; i += 2)
                {
                    if (!TryCoordinate(shell, args[i], out var x) || !TryCoordinate(shell, args[i + 1], out var y))
                        return;

                    waypoints.Add(new EntityCoordinates(map, x, y));
                }

                _pilot.GoTo((uid, duty), waypoints);
                break;
            case "loiter" when args.Length == 6:
                if (!TryCoordinate(shell, args[3], out var cx)
                    || !TryCoordinate(shell, args[4], out var cy)
                    || !TryNumber(shell, args[5], out var radius))
                {
                    return;
                }

                if (radius is < 1 or > WGCrewLimits.MaxRange)
                {
                    shell.WriteError(Loc.GetString("cmd-wg_crew-bad-radius", ("arg", args[5])));
                    return;
                }

                _pilot.Loiter((uid, duty), new EntityCoordinates(map, cx, cy), radius);
                break;
            case "follow" when args.Length == 4:
                if (!TryGrid(shell, args[3], out var grid) || !TryTarget(shell, uid, name, map, grid, "cmd-wg_crew-follow-own-grid"))
                    return;

                _pilot.Follow((uid, duty), grid, duty.FollowRange);
                break;
            case "dock" when args.Length == 4:
                if (!TryGrid(shell, args[3], out var target) || !TryTarget(shell, uid, name, map, target, "cmd-wg_crew-dock-own-grid"))
                    return;

                _pilot.Dock((uid, duty), target);
                break;
            case "undock" when args.Length == 3:
                _pilot.Undock((uid, duty));
                break;
            default:
                shell.WriteLine(Help);
                return;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_crew-orders-set",
            ("name", name),
            ("orders", Loc.GetString($"wg-crew-order-{duty.Orders.ToString().ToLowerInvariant()}"))));
    }

    /// <summary>wg_crew callsign &lt;mob&gt; &lt;text...&gt;</summary>
    private void Callsign(IConsoleShell shell, string[] args)
    {
        if (args.Length < 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[1], out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.TryGetComponent<WGRadioOperatorComponent>(uid, out var radio))
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-not-radio-operator", ("arg", args[1])));
            return;
        }

        _radio.SetCallsign((uid.Value, radio), string.Join(' ', args[2..]));
        shell.WriteLine(Loc.GetString("cmd-wg_crew-callsign-set",
            ("name", EntityManager.GetComponent<MetaDataComponent>(uid.Value).EntityName),
            ("callsign", radio.Callsign ?? string.Empty)));
    }

    /// <summary>A grid by entity id, or "here" for the caller's own.</summary>
    private bool TryGrid(IConsoleShell shell, string arg, out EntityUid grid)
    {
        grid = default;
        if (arg == "here")
        {
            if (shell.Player?.AttachedEntity is not { Valid: true } player
                || EntityManager.GetComponent<TransformComponent>(player).GridUid is not { } here)
            {
                shell.WriteError(Loc.GetString("cmd-wg_crew-not-on-grid"));
                return false;
            }

            grid = here;
            return true;
        }

        if (!NetEntity.TryParse(arg, out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.HasComponent<MapGridComponent>(uid))
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-not-a-grid", ("arg", arg)));
            return false;
        }

        grid = uid.Value;
        return true;
    }

    /// <summary>The optional deckhand count at an argument index, default 2.</summary>
    private bool TryCount(IConsoleShell shell, string[] args, int index, out int count)
    {
        count = 2;
        if (args.Length <= index)
            return true;

        if (int.TryParse(args[index], out count) && count >= 0)
            return true;

        shell.WriteError(Loc.GetString("cmd-wg_crew-bad-number", ("arg", args[index])));
        return false;
    }

    /// <summary>A decimal number, written with a point whatever the server's culture.</summary>
    private bool TryNumber(IConsoleShell shell, string arg, out float value)
    {
        if (Parse.TryFloat(arg, out value) && float.IsFinite(value))
            return true;

        shell.WriteError(Loc.GetString("cmd-wg_crew-bad-decimal", ("arg", arg)));
        return false;
    }

    /// <summary>A map coordinate: a finite number within the allowed bound.</summary>
    private bool TryCoordinate(IConsoleShell shell, string arg, out float value)
    {
        if (!TryNumber(shell, arg, out value))
            return false;

        if (MathF.Abs(value) <= WGCrewLimits.MaxCoordinate)
            return true;

        shell.WriteError(Loc.GetString("cmd-wg_crew-coordinate-range", ("arg", arg)));
        return false;
    }

    /// <summary>A follow or dock target must be on the pilot's map and not the pilot's own grid.</summary>
    private bool TryTarget(IConsoleShell shell, EntityUid pilot, string name, EntityUid map, EntityUid target, string ownGridKey)
    {
        if (target == EntityManager.GetComponent<TransformComponent>(pilot).GridUid)
        {
            shell.WriteError(Loc.GetString(ownGridKey, ("name", name)));
            return false;
        }

        if (EntityManager.GetComponent<TransformComponent>(target).MapUid != map)
        {
            shell.WriteError(Loc.GetString("cmd-wg_crew-target-other-map", ("name", name)));
            return false;
        }

        return true;
    }

    /// <summary>A role by id, or by its id without the WGCrew prefix, case-insensitive.</summary>
    private bool TryRole(string arg, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WGCrewRolePrototype? role)
    {
        if (_prototypes.TryIndex(arg, out role))
            return true;

        role = _prototypes.EnumeratePrototypes<WGCrewRolePrototype>()
            .FirstOrDefault(candidate => string.Equals(candidate.ID, arg, StringComparison.OrdinalIgnoreCase)
                                         || string.Equals(candidate.ID, "WGCrew" + arg, StringComparison.OrdinalIgnoreCase));
        return role != null;
    }

    private static string DefaultGroup(EntityUid grid)
    {
        return $"crew-{grid.Id}";
    }
}
