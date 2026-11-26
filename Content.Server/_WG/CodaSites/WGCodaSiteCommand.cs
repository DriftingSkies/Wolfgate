using Content.Server.Administration;
using Content.Shared._WG.CodaSites;
using Content.Shared._WG.Lattice;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.CodaSites;

/// <summary>`wgcodasite place` places any Coda site a Lattice map lacks; `wgcodasite where` lists where each one is.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed class WGCodaSiteCommand : LocalizedEntityCommands
{
    [Dependency] private WGCodaSiteSystem _sites = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override string Command => "wgcodasite";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || args[0] is not ("place" or "where"))
        {
            shell.WriteError(Loc.GetString("cmd-wgcodasite-help"));
            return;
        }

        var fields = EntityManager.EntityQueryEnumerator<WGLatticeFieldComponent>();
        while (fields.MoveNext(out var map, out _))
        {
            foreach (var site in _proto.EnumeratePrototypes<WGCodaSitePrototype>())
            {
                if (_sites.SiteOn(map, site.ID) is { } grid)
                {
                    shell.WriteLine(Loc.GetString("cmd-wgcodasite-at", ("site", site.ID), ("position", _transform.GetWorldPosition(grid).ToString())));
                    continue;
                }

                if (args[0] == "place")
                    shell.WriteLine(_sites.TryPlace(map, site, out var placed)
                        ? Loc.GetString("cmd-wgcodasite-placed", ("site", site.ID), ("position", _transform.GetWorldPosition(placed).ToString()))
                        : Loc.GetString("cmd-wgcodasite-no-place", ("site", site.ID)));
                else
                    shell.WriteLine(Loc.GetString("cmd-wgcodasite-not-placed", ("site", site.ID)));
            }
        }
    }
}
