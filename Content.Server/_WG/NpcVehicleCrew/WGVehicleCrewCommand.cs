using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Server.Administration;
using Content.Shared._WG.NpcVehicleCrew;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcVehicleCrew;

/// <summary>NPC vehicle crews from the console: spawn one beside you, list them, clear a group or all.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class WGVehicleCrewCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WGVehicleCrewSystem _crews = default!;

    private static readonly string[] Subcommands = { "spawn", "list", "clear" };

    /// <summary>The behaviours a crew can be spawned with; Retreat and Bail are reached, not ordered.</summary>
    private static readonly string[] SpawnBehaviours =
    {
        nameof(WGVehicleBehaviour.Hold),
        nameof(WGVehicleBehaviour.Patrol),
        nameof(WGVehicleBehaviour.Hunt),
        nameof(WGVehicleBehaviour.Escort),
        nameof(WGVehicleBehaviour.Flee),
    };

    private const float DefaultDistance = 6f;

    public override string Command => "wg_vehiclecrew";

    public override string Description => Loc.GetString("wg-vehiclecrew-desc");

    public override string Help => Loc.GetString("wg-vehiclecrew-help");

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case "spawn":
                Spawn(shell, args);
                break;
            case "list":
                List(shell);
                break;
            case "clear":
                Clear(shell, args);
                break;
            default:
                shell.WriteError(Loc.GetString("wg-vehiclecrew-unknown-sub", ("sub", args[0])));
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("wg-vehiclecrew-hint-sub")),
            2 when args[0] == "spawn" => CompletionResult.FromHintOptions(
                _prototypes.EnumeratePrototypes<WGVehicleCrewPrototype>().Select(crew => crew.ID).Order(),
                Loc.GetString("wg-vehiclecrew-hint-crew")),
            2 when args[0] == "clear" => CompletionResult.FromHintOptions(
                _crews.GetCrews().Select(crew => crew.Comp.Group).Distinct().Order(),
                Loc.GetString("wg-vehiclecrew-hint-group")),
            3 when args[0] == "spawn" => CompletionResult.FromHintOptions(SpawnBehaviours, Loc.GetString("wg-vehiclecrew-hint-behaviour")),
            4 when args[0] == "spawn" => CompletionResult.FromHint(Loc.GetString("wg-vehiclecrew-hint-distance")),
            _ => CompletionResult.Empty,
        };
    }

    /// <summary>wg_vehiclecrew spawn &lt;crew&gt; [behaviour] [distance]: beside the caller, on his grid.</summary>
    private void Spawn(IConsoleShell shell, string[] args)
    {
        if (args.Length is < 2 or > 4)
        {
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { } player)
        {
            shell.WriteError(Loc.GetString("wg-vehiclecrew-no-entity"));
            return;
        }

        if (!_prototypes.HasIndex<WGVehicleCrewPrototype>(args[1]))
        {
            shell.WriteError(Loc.GetString("wg-vehiclecrew-unknown-crew", ("crew", args[1])));
            return;
        }

        WGVehicleBehaviour? behaviour = null;
        if (args.Length >= 3)
        {
            if (!Enum.TryParse<WGVehicleBehaviour>(args[2], true, out var parsed) || !SpawnBehaviours.Contains(parsed.ToString()))
            {
                shell.WriteError(Loc.GetString("wg-vehiclecrew-bad-behaviour", ("behaviour", args[2])));
                return;
            }

            behaviour = parsed;
        }

        var distance = DefaultDistance;
        if (args.Length == 4 &&
            (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out distance) || distance < 0f))
        {
            shell.WriteError(Loc.GetString("wg-vehiclecrew-bad-distance"));
            return;
        }

        var coords = EntityManager.GetComponent<TransformComponent>(player).Coordinates.Offset(new Vector2(distance, 0f));
        if (!_crews.TrySpawn(args[1], coords, out var vehicle, behaviour) ||
            !EntityManager.TryGetComponent<WGVehicleCrewComponent>(vehicle, out var crew))
        {
            shell.WriteError(Loc.GetString("wg-vehiclecrew-spawn-failed", ("crew", args[1])));
            return;
        }

        shell.WriteLine(Loc.GetString("wg-vehiclecrew-spawned",
            ("vehicle", EntityManager.ToPrettyString(vehicle)),
            ("group", crew.Group)));
    }

    /// <summary>wg_vehiclecrew list</summary>
    private void List(IConsoleShell shell)
    {
        var crews = _crews.GetCrews();
        if (crews.Count == 0)
        {
            shell.WriteLine(Loc.GetString("wg-vehiclecrew-list-empty"));
            return;
        }

        foreach (var (uid, crew) in crews)
        {
            // RA0002: read the member into a local before calling on it.
            var behaviour = crew.Behaviour;
            shell.WriteLine(Loc.GetString(crew.Bailed ? "wg-vehiclecrew-list-line-bailed" : "wg-vehiclecrew-list-line",
                ("vehicle", EntityManager.ToPrettyString(uid)),
                ("crew", crew.Prototype.Id),
                ("group", crew.Group),
                ("behaviour", behaviour.ToString()),
                ("alive", _crews.CountAlive(crew))));
        }
    }

    /// <summary>wg_vehiclecrew clear [group]</summary>
    private void Clear(IConsoleShell shell, string[] args)
    {
        if (args.Length > 2)
        {
            shell.WriteLine(Help);
            return;
        }

        var count = _crews.Clear(args.Length == 2 ? args[1] : null);
        shell.WriteLine(Loc.GetString("wg-vehiclecrew-cleared", ("count", count)));
    }
}
