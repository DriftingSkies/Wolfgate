using Content.Server.Administration;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Siege;
using Content.Shared._WF.Lattice;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WF.Lattice;

/// <summary>Brings on the Lattice's end, or a Scion's flesh ships, on the caller's map or the first sector.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed class WFLatticeEndCommand : LocalizedEntityCommands
{
    [Dependency] private WFLatticeSystem _lattice = default!;
    [Dependency] private WFSiegeSystem _siege = default!;

    public override string Command => "wflattice";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !TryGetField(shell, out var map))
        {
            shell.WriteLine(Help);
            return;
        }

        switch (args[0])
        {
            case "unstring":
                _lattice.Unstring(map);
                break;
            case "tacet":
                _lattice.Tacet(map);
                break;
            case "siege":
                var query = EntityManager.EntityQueryEnumerator<WFMonolithComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var monolith, out var xform))
                {
                    if (xform.MapUid != map.Owner || monolith.Local)
                        continue;

                    shell.WriteLine(Loc.GetString("cmd-wflattice-siege-sent", ("count", _siege.SendWave((uid, monolith), map))));
                    return;
                }

                shell.WriteError(Loc.GetString("cmd-wflattice-no-scion"));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    private bool TryGetField(IConsoleShell shell, out Entity<WFLatticeFieldComponent> map)
    {
        map = default;
        if (shell.Player?.AttachedEntity is { } mob
            && EntityManager.GetComponent<TransformComponent>(mob).MapUid is { } mobMap
            && EntityManager.TryGetComponent<WFLatticeFieldComponent>(mobMap, out var here))
        {
            map = (mobMap, here);
            return true;
        }

        var fields = EntityManager.EntityQueryEnumerator<WFLatticeFieldComponent>();
        if (!fields.MoveNext(out var uid, out var field))
        {
            shell.WriteError(Loc.GetString("cmd-wflattice-no-field"));
            return false;
        }

        map = (uid, field);
        return true;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromOptions(new[] { "siege", "unstring", "tacet" })
            : CompletionResult.Empty;
    }
}
