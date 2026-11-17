using System.Linq;
using Content.Server._WG.SectorControl.Systems;
using Content.Server.Administration;
using Content.Shared._WG.SectorControl;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WG.SectorControl.Commands;

/// <summary>Sector territory from the console: show it, claim, free, contest or challenge a cell, clear it, name a cell.</summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class WGSectorCommand : LocalizedEntityCommands
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WGSectorTerritorySystem _territory = default!;

    private static readonly string[] Subcommands = { "status", "claim", "free", "contest", "challenge", "clear", "callsign" };

    public override string Command => "wg_sector";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var map = _territory.SectorMap;
        switch (args.Length > 0 ? args[0] : string.Empty)
        {
            case "status" when args.Length == 1:
                Status(shell, map);
                break;
            case "claim" when args.Length == 4:
                if (TryFaction(shell, args[1], out var claimant) && TryCell(shell, args[2], args[3], out var claimed))
                {
                    shell.WriteLine(Loc.GetString(_territory.TryClaim(map, claimed, claimant, null)
                        ? "cmd-wg_sector-claimed"
                        : "cmd-wg_sector-claim-failed", ("faction", claimant.Id), ("callsign", WGSectorHex.Callsign(claimed))));
                }
                break;
            case "free" when args.Length == 3:
                if (TryCell(shell, args[1], args[2], out var freed))
                {
                    // Both run: a held cell can be contested too.
                    var released = _territory.Free(map, freed);
                    released |= _territory.DropContest(map, freed);
                    shell.WriteLine(Loc.GetString(released
                        ? "cmd-wg_sector-freed"
                        : "cmd-wg_sector-not-held", ("callsign", WGSectorHex.Callsign(freed))));
                }
                break;
            case "contest" when args.Length == 5:
                Contest(shell, map, args);
                break;
            case "challenge" when args.Length == 5:
                Challenge(shell, map, args);
                break;
            case "clear" when args.Length is 1 or 2:
                ProtoId<WGSectorFactionPrototype>? only = null;
                if (args.Length == 2)
                {
                    if (!TryFaction(shell, args[1], out var faction))
                        break;

                    only = faction;
                }

                shell.WriteLine(Loc.GetString("cmd-wg_sector-cleared", ("count", _territory.Clear(map, only))));
                break;
            case "callsign" when args.Length == 3:
                if (!float.TryParse(args[1], out var x) || !float.TryParse(args[2], out var y) || !float.IsFinite(x) || !float.IsFinite(y))
                {
                    shell.WriteLine(Loc.GetString("cmd-wg_sector-bad-position", ("x", args[1]), ("y", args[2])));
                    break;
                }

                var cell = _territory.CellOf(new MapCoordinates(x, y, map));
                var centre = WGSectorHex.Centre(cell, _territory.CellSize);
                shell.WriteLine(Loc.GetString("cmd-wg_sector-callsign",
                    ("callsign", WGSectorHex.Callsign(cell)),
                    ("q", cell.Q),
                    ("r", cell.R),
                    ("x", centre.X.ToString("0")),
                    ("y", centre.Y.ToString("0"))));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        var factions = _prototypes.EnumeratePrototypes<WGSectorFactionPrototype>().Select(faction => faction.ID).Order();
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(Subcommands, Loc.GetString("cmd-wg_sector-hint-sub")),
            2 when args[0] is "claim" or "contest" or "challenge" or "clear" =>
                CompletionResult.FromHintOptions(factions, Loc.GetString("cmd-wg_sector-hint-faction")),
            2 when args[0] == "free" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-q")),
            3 when args[0] == "free" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-r")),
            3 when args[0] is "claim" or "contest" or "challenge" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-q")),
            4 when args[0] is "claim" or "contest" or "challenge" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-r")),
            5 when args[0] == "contest" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-minutes")),
            5 when args[0] == "challenge" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-challenge-minutes")),
            2 when args[0] == "callsign" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-x")),
            3 when args[0] == "callsign" => CompletionResult.FromHint(Loc.GetString("cmd-wg_sector-hint-y")),
            _ => CompletionResult.Empty,
        };
    }

    private void Status(IConsoleShell shell, MapId map)
    {
        shell.WriteLine(Loc.GetString("cmd-wg_sector-status-header",
            ("enabled", _territory.Enabled.ToString()),
            ("map", map.ToString()),
            ("size", _territory.CellSize.ToString("0"))));

        foreach (var faction in _prototypes.EnumeratePrototypes<WGSectorFactionPrototype>().OrderBy(faction => faction.ID))
        {
            var held = _territory.Held(map, faction.ID);
            if (held.Count == 0)
                continue;

            shell.WriteLine(Loc.GetString("cmd-wg_sector-status-faction",
                ("faction", faction.ID),
                ("name", Loc.GetString(faction.Name)),
                ("cells", held.Count),
                ("callsigns", string.Join(", ", held.Select(WGSectorHex.Callsign).Order()))));
        }

        if (_territory.Territory(map) is not { } territory)
            return;

        foreach (var (cell, contest) in territory.Contests)
        {
            shell.WriteLine(Loc.GetString(contest.Holder == null ? "cmd-wg_sector-status-contest" : "cmd-wg_sector-status-challenge",
                ("callsign", WGSectorHex.Callsign(cell)),
                ("faction", contest.Faction.Id),
                ("holder", contest.Holder?.Id ?? string.Empty),
                ("seconds", Math.Max(0, (int) (contest.Deadline - _timing.CurTime).TotalSeconds))));
        }

        foreach (var (cell, retake) in territory.Retakes)
        {
            shell.WriteLine(Loc.GetString("cmd-wg_sector-status-retake",
                ("callsign", WGSectorHex.Callsign(cell)),
                ("seconds", Math.Max(0, (int) (retake.Deadline - _timing.CurTime).TotalSeconds))));
        }
    }

    /// <summary>wg_sector contest &lt;faction&gt; &lt;q&gt; &lt;r&gt; &lt;minutes&gt;: a contest decided that many minutes from now.</summary>
    private void Contest(IConsoleShell shell, MapId map, string[] args)
    {
        if (!TryFaction(shell, args[1], out var faction) || !TryCell(shell, args[2], args[3], out var cell) || !TryMinutes(shell, args[4], out var minutes))
            return;

        shell.WriteLine(Loc.GetString(_territory.Contest(map, cell, faction, _timing.CurTime + TimeSpan.FromMinutes(minutes))
            ? "cmd-wg_sector-contested"
            : "cmd-wg_sector-contest-failed", ("faction", faction.Id), ("callsign", WGSectorHex.Callsign(cell))));
    }

    /// <summary>wg_sector challenge &lt;faction&gt; &lt;q&gt; &lt;r&gt; &lt;minutes&gt;: a challenge on another faction's cell, with no source.</summary>
    private void Challenge(IConsoleShell shell, MapId map, string[] args)
    {
        if (!TryFaction(shell, args[1], out var faction) || !TryCell(shell, args[2], args[3], out var cell) || !TryMinutes(shell, args[4], out var minutes))
            return;

        shell.WriteLine(Loc.GetString(_territory.Challenge(map, cell, faction, _timing.CurTime + TimeSpan.FromMinutes(minutes), null)
            ? "cmd-wg_sector-challenged"
            : "cmd-wg_sector-challenge-failed", ("faction", faction.Id), ("callsign", WGSectorHex.Callsign(cell))));
    }

    private bool TryMinutes(IConsoleShell shell, string arg, out float minutes)
    {
        if (float.TryParse(arg, out minutes) && float.IsFinite(minutes) && minutes is >= 0 and <= 600)
            return true;

        shell.WriteLine(Loc.GetString("cmd-wg_sector-bad-minutes", ("arg", arg)));
        return false;
    }

    private bool TryFaction(IConsoleShell shell, string arg, out ProtoId<WGSectorFactionPrototype> faction)
    {
        faction = arg;
        if (_prototypes.HasIndex(faction))
            return true;

        shell.WriteLine(Loc.GetString("cmd-wg_sector-unknown-faction", ("faction", arg)));
        return false;
    }

    private bool TryCell(IConsoleShell shell, string q, string r, out WGSectorCell cell)
    {
        cell = default;
        if (!int.TryParse(q, out var column) || !int.TryParse(r, out var row))
        {
            shell.WriteLine(Loc.GetString("cmd-wg_sector-bad-cell", ("q", q), ("r", r)));
            return false;
        }

        cell = new WGSectorCell(column, row);
        return true;
    }
}
