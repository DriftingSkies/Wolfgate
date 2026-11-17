using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's roster from the admin console (the ship command console's Crew tab does the same): list the
/// posts, staff one or every empty post, stand a post down or up, or switch automatic restaffing.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class WGShipCrewCommand : LocalizedEntityCommands
{
    [Dependency] private WGShipCommandSystem _command = default!;
    [Dependency] private WGShipRosterSystem _roster = default!;

    public override string Command => "wg_shipcrew";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!WGShipCommandConsole.TryGetGrid<WGShipRosterComponent>(shell, EntityManager, args[1], out var grid, out var roster)
            || !WGShipCommandConsole.CanConfigure(shell, _command, grid))
            return;

        var ent = (grid, roster);
        switch (args[0])
        {
            case "list" when args.Length == 2:
                shell.WriteLine(Loc.GetString("cmd-wg_shipcrew-auto", ("auto", roster.AutoRestaff), ("cap", roster.RestaffsPerHour), ("delay", (int) roster.RestaffDelay.TotalSeconds)));
                foreach (var slot in roster.Slots.Values)
                {
                    var state = slot.Holder is { } holder && EntityManager.EntityExists(holder) ? "player"
                        : slot.StoodDown ? "down"
                        : _roster.Held(slot) ? "held"
                        : "empty";
                    var who = slot.Holder ?? slot.Occupant;
                    shell.WriteLine(Loc.GetString("cmd-wg_shipcrew-line",
                        ("slot", slot.Id),
                        ("role", slot.What),
                        ("state", state),
                        ("who", who is { } mob && EntityManager.EntityExists(mob) ? EntityManager.GetComponent<MetaDataComponent>(mob).EntityName : "-")));
                }
                break;
            case "staff" when args.Length is 2 or 3:
                var only = args.Length == 3 && args[2] != "all" ? args[2] : null;
                if (only != null && !roster.Slots.ContainsKey(only))
                {
                    shell.WriteError(Loc.GetString("cmd-wg_shipcrew-no-slot", ("slot", only)));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wg_shipcrew-staffed", ("count", _roster.Staff(ent, only, shell.Player?.AttachedEntity))));
                break;
            case "down" or "up" when args.Length == 3:
                if (!roster.Slots.ContainsKey(args[2]))
                {
                    shell.WriteError(Loc.GetString("cmd-wg_shipcrew-no-slot", ("slot", args[2])));
                    return;
                }

                _roster.SetStoodDown(ent, args[2], args[0] == "down", shell.Player?.AttachedEntity);
                shell.WriteLine(Loc.GetString("cmd-wg_shipcrew-stood", ("slot", args[2]), ("down", args[0] == "down")));
                break;
            case "auto" when args.Length == 3 && args[2] is "on" or "off":
                _roster.SetAutoRestaff(ent, args[2] == "on", shell.Player?.AttachedEntity);
                shell.WriteLine(Loc.GetString("cmd-wg_shipcrew-auto-set", ("auto", roster.AutoRestaff)));
                break;
            default:
                shell.WriteLine(Help);
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "list", "staff", "down", "up", "auto" }, Loc.GetString("cmd-wg_shipcrew-hint-sub")),
            2 => CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wg_ship-hint-grid")),
            3 when args[0] == "auto" => CompletionResult.FromHintOptions(new[] { "on", "off" }, Loc.GetString("cmd-wg_shipcrew-hint-auto")),
            3 when args[0] is "staff" or "down" or "up" => CompletionResult.FromHint(Loc.GetString("cmd-wg_shipcrew-hint-slot")),
            _ => CompletionResult.Empty,
        };
    }
}
