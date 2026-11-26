using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Pyramid;

/// <summary>`wgpyramid summon` brings the Pyramid in on the ship you stand on; `wgpyramid collapse` brings every one down.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class WGPyramidCommand : LocalizedEntityCommands
{
    [Dependency] private WGPyramidSystem _pyramid = default!;

    public override string Command => "wgpyramid";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length == 1 ? args[0] : null)
        {
            case "summon":
                if (shell.Player?.AttachedEntity is not { } mob
                    || EntityManager.GetComponent<TransformComponent>(mob).GridUid is not { } grid)
                {
                    shell.WriteError(Loc.GetString("cmd-wgpyramid-no-ship"));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wgpyramid-summoned", ("pyramid", EntityManager.ToPrettyString(_pyramid.Summon(grid)))));
                return;
            case "collapse":
                var query = EntityManager.EntityQueryEnumerator<WGPyramidComponent>();
                while (query.MoveNext(out var uid, out var comp))
                    _pyramid.StartCollapse((uid, comp));
                return;
            default:
                shell.WriteError(Loc.GetString("cmd-wgpyramid-help"));
                return;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(new[] { "summon", "collapse" }, Loc.GetString("cmd-wgpyramid-help"))
            : CompletionResult.Empty;
    }
}
