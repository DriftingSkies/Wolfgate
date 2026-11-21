using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.EntityCensus;

/// <summary>Prints the last entity census, or takes one on the spot with <c>now</c>.</summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class WGEntityCensusCommand : LocalizedEntityCommands
{
    [Dependency] private WGEntityCensusSystem _census = default!;

    public override string Command => "wg_entcensus";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var now = args.Contains("now");
        var rows = 20;
        foreach (var arg in args)
        {
            if (int.TryParse(arg, out var parsed))
                rows = Math.Clamp(parsed, 1, 200);
        }

        var census = now ? _census.TakeNow() : _census.Last;
        if (census == null)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_entcensus-none"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-wg_entcensus-total",
            ("total", census.Total), ("contained", census.Contained), ("time", census.Taken.ToString(@"hh\:mm\:ss"))));

        shell.WriteLine(Loc.GetString("cmd-wg_entcensus-kinds"));
        for (var i = 0; i < (int) WGCensusKind.Count; i++)
        {
            shell.WriteLine($"  {(WGCensusKind) i,-12} {census.Kinds[i],8} ({census.Paused[i]} paused)");
        }

        shell.WriteLine(Loc.GetString("cmd-wg_entcensus-prototypes"));
        foreach (var (id, count) in census.Prototypes.OrderByDescending(pair => pair.Value).Take(rows))
        {
            shell.WriteLine($"  {count,8} {id}");
        }

        shell.WriteLine(Loc.GetString("cmd-wg_entcensus-grids"));
        foreach (var (name, kind, count) in census.Grids.Take(rows))
        {
            shell.WriteLine($"  {count,8} {kind,-10} {name}");
        }

        if (census.Spawned.Count == 0)
            return;

        shell.WriteLine(Loc.GetString("cmd-wg_entcensus-spawned", ("seconds", (int) census.Span)));
        foreach (var (id, count) in census.Spawned.OrderByDescending(pair => pair.Value).Take(rows))
        {
            shell.WriteLine($"  +{count,7} -{census.Deleted.GetValueOrDefault(id),7} {id}");
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(["now"], Loc.GetString("cmd-wg_entcensus-hint"))
            : CompletionResult.Empty;
    }
}
