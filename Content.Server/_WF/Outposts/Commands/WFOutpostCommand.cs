using System.Linq;
using Content.Server._NF.Bank;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._WF.Outposts.Commands;

/// <summary>
/// Admin handling of a player's outpost saves: list them, make one loadable again this round, refund what a load cost,
/// or delete one.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class WFOutpostCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;

    public override string Command => "wfoutpost";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || args[0] is not ("list" or "restore" or "refund" or "delete"))
        {
            shell.WriteError(Loc.GetString("cmd-wfoutpost-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[1]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-wfoutpost-no-player", ("player", args[1])));
            return;
        }

        var user = located.UserId;
        if (args[0] == "list")
        {
            var saves = await _db.GetOutpostSavesAsync(user);
            if (saves.Count == 0)
                shell.WriteLine(Loc.GetString("cmd-wfoutpost-none", ("player", located.Username)));
            foreach (var save in saves)
            {
                shell.WriteLine(Loc.GetString("cmd-wfoutpost-entry", ("slot", save.Slot + 1), ("name", save.Name),
                    ("tiles", save.Tiles), ("entities", save.Entities), ("appraisal", save.Appraisal),
                    ("saved", save.SavedRound), ("loaded", save.LoadedRound), ("lineage", save.Lineage)));
            }

            return;
        }

        if (args.Length < 3 || !int.TryParse(args[2], out var slot) || slot < 1 || slot > Content.Shared._WF.Outposts.WFOutpostLimits.SaveSlots)
        {
            shell.WriteError(Loc.GetString("cmd-wfoutpost-bad-slot"));
            return;
        }

        slot--;
        if (await _db.GetOutpostSaveDataAsync(user, slot) is not { } row)
        {
            shell.WriteError(Loc.GetString("cmd-wfoutpost-empty"));
            return;
        }

        switch (args[0])
        {
            case "restore":
                await _db.SetOutpostSaveAsync(user, row.Save with { SavedRound = 0, LoadedRound = 0 }, row.Data);
                shell.WriteLine(Loc.GetString("cmd-wfoutpost-restored", ("name", row.Save.Name)));
                break;
            case "refund":
                var price = WFOutpostSystem.LoadPrice(row.Save.Appraisal);
                if (!_players.TryGetSessionById(user, out var session) || session.AttachedEntity is not { } body
                    || !_bank.TryBankDeposit(body, price, tax: false))
                {
                    shell.WriteError(Loc.GetString("cmd-wfoutpost-refund-failed"));
                    return;
                }

                shell.WriteLine(Loc.GetString("cmd-wfoutpost-refunded", ("price", price), ("player", located.Username)));
                break;
            case "delete":
                await _db.DeleteOutpostSaveAsync(user, slot);
                shell.WriteLine(Loc.GetString("cmd-wfoutpost-deleted", ("name", row.Save.Name)));
                break;
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} ran wfoutpost {args[0]} on save slot {slot + 1} ({row.Save.Name}) of {located.Username}");
        _outposts.ForgetSlots(user);
        _outposts.RefreshOwnerUi(user);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "list", "restore", "refund", "delete" }, Loc.GetString("cmd-wfoutpost-hint-action")),
            2 => CompletionResult.FromHintOptions(_players.Sessions.Select(s => s.Name).OrderBy(n => n), Loc.GetString("cmd-wfoutpost-hint-player")),
            3 => CompletionResult.FromHint(Loc.GetString("cmd-wfoutpost-hint-slot")),
            _ => CompletionResult.Empty,
        };
    }
}
