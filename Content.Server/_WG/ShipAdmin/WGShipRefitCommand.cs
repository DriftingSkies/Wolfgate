using System.Linq;
using Content.Server.Administration;
using Content.Shared._WG.Drydock;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipAdmin;

/// <summary>Brings one refit line of any ship up to a tier, free, as Shipmaster's refit does.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGShipRefitCommand : LocalizedEntityCommands
{
    [Dependency] private WGShipAdminSystem _ships = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public const string Here = "here";

    public override string Command => "shiprefit";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (args.Length != 3 || !int.TryParse(args[2], out var tier))
        {
            shell.WriteLine(Help);
            return;
        }

        if (ResolveGrid(EntityManager, shell, args[0]) is not { } grid)
        {
            shell.WriteError(Loc.GetString("cmd-shipmaster-not-grid", ("grid", args[0])));
            return;
        }

        var ok = _ships.StartRefit(player, grid, args[1], tier, out var result);
        if (ok)
            shell.WriteLine(result);
        else
            shell.WriteError(result);
    }

    /// <summary>A grid named by entity id, or the one the caller is on for "here".</summary>
    public static EntityUid? ResolveGrid(IEntityManager entities, IConsoleShell shell, string arg)
    {
        EntityUid? grid = null;
        if (arg == Here)
        {
            if (shell.Player?.AttachedEntity is { } body)
                grid = entities.GetComponent<TransformComponent>(body).GridUid;
        }
        else if (NetEntity.TryParse(arg, out var net) && entities.TryGetEntity(net, out var named))
        {
            grid = named;
        }

        return grid != null && entities.HasComponent<MapGridComponent>(grid) ? grid : null;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        switch (args.Length)
        {
            case 1:
                return CompletionResult.FromHintOptions(new[] { Here }, Loc.GetString("cmd-shiprefit-hint-grid"));
            case 2:
                var refits = _prototype.EnumeratePrototypes<WGShipRefitPrototype>()
                    .OrderBy(p => p.ID)
                    .Select(p => new CompletionOption(p.ID, Loc.GetString(p.Name)));
                return CompletionResult.FromHintOptions(refits, Loc.GetString("cmd-shiprefit-hint-refit"));
            case 3:
                var tiers = _prototype.TryIndex<WGShipRefitPrototype>(args[1], out var refit)
                    ? Enumerable.Range(1, refit.Tiers.Count).Select(t => t.ToString())
                    : Enumerable.Empty<string>();
                return CompletionResult.FromHintOptions(tiers, Loc.GetString("cmd-shiprefit-hint-tier"));
            default:
                return CompletionResult.Empty;
        }
    }
}
