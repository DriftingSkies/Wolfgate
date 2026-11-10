using Content.Server._WF.Outposts;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;

namespace Content.Server._WF.Hideouts;

/// <summary>
/// Admin handling of a player's outpost modules: finish the level being built, or set a module's level outright.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class WFHideoutCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private WFHideoutSystem _hideouts = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;

    public override string Command => "wfhideout";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || args[0] is not ("finish" or "setlevel"))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideout-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[1]);
        if (located == null || !_outposts.TryGetOutpost(located.UserId, out var outpost))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideout-no-outpost", ("player", args[1])));
            return;
        }

        if (args[0] == "finish")
        {
            if (!_hideouts.SkipBuild(outpost))
            {
                shell.WriteError(Loc.GetString("cmd-wfhideout-not-building"));
                return;
            }

            _adminLog.Add(LogType.WFOutpost, LogImpact.Medium, $"{shell.Player?.Name ?? "Console"} finished the build on outpost {outpost.Owner}");
            shell.WriteLine(Loc.GetString("cmd-wfhideout-finished"));
            return;
        }

        if (args.Length < 4 || !int.TryParse(args[3], out var level) || !_hideouts.SetLevel(outpost, args[2], level))
        {
            shell.WriteError(Loc.GetString("cmd-wfhideout-bad-level"));
            return;
        }

        _adminLog.Add(LogType.WFOutpost, LogImpact.Medium,
            $"{shell.Player?.Name ?? "Console"} set {args[2]} to level {level} on outpost {outpost.Owner}");
        shell.WriteLine(Loc.GetString("cmd-wfhideout-level-set", ("module", args[2]), ("level", level)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromOptions(new[] { "finish", "setlevel" }),
            2 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("cmd-wfhideout-hint-player")),
            3 when args[0] == "setlevel" => CompletionResult.FromHintOptions(
                CompletionHelper.PrototypeIDs<Content.Shared._WF.Hideouts.WFHideoutModulePrototype>(), Loc.GetString("cmd-wfhideout-hint-module")),
            4 when args[0] == "setlevel" => CompletionResult.FromHint(Loc.GetString("cmd-wfhideout-hint-level")),
            _ => CompletionResult.Empty,
        };
    }
}
