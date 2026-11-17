using Content.Server.Administration;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared._WG.ShipCommand;
using Robust.Shared.Console;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// Your own presets for a commanded ship, from the admin console (the ship command console's Presets tab does the
/// same for the owner): list them, save the ship's settings under a name, load one onto the ship, or delete one.
/// Admins only, and only while the owner lets admins configure the ship; each sees only their own presets.
/// </summary>
[AdminCommand(AdminFlags.Spawn)]
public sealed partial class WGShipPresetCommand : LocalizedEntityCommands
{
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private WGShipCommandSystem _command = default!;
    [Dependency] private WGShipPresetSystem _presets = default!;

    public override string Command => "wg_shippreset";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || args[0] is not ("list" or "save" or "load" or "delete") || args[0] != "list" && args.Length != 3)
        {
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("cmd-wg_shippreset-players-only"));
            return;
        }

        if (!WGShipCommandConsole.TryGetGrid<WGShipCommandComponent>(shell, EntityManager, args[1], out var grid, out _)
            || !WGShipCommandConsole.CanConfigure(shell, _command, grid))
            return;

        if (_presets.VesselOf(grid) is not { } vessel)
        {
            shell.WriteError(Loc.GetString("cmd-wg_shippreset-no-vessel"));
            return;
        }

        var name = args.Length == 3 ? args[2] : string.Empty;
        if (args[0] != "list" && !WGShipPresetSystem.ValidName(name))
        {
            shell.WriteError(Loc.GetString("cmd-wg_shippreset-bad-name", ("max", WGShipPresetSystem.MaxNameLength)));
            return;
        }

        try
        {
            switch (args[0])
            {
                case "list":
                    var presets = await _db.GetShipCommandPresetsAsync(player.UserId.UserId, vessel);
                    if (presets.Count == 0)
                        shell.WriteLine(Loc.GetString("cmd-wg_shippreset-none", ("vessel", vessel)));
                    foreach (var preset in presets)
                        shell.WriteLine(Loc.GetString("cmd-wg_shippreset-line", ("name", preset.Name), ("saved", preset.Saved.ToString("yyyy-MM-dd HH:mm"))));
                    break;
                case "save":
                    var stored = await _db.GetShipCommandPresetsAsync(player.UserId.UserId, vessel);
                    if (stored.Count >= WGShipConsoleSystem.MaxPresets && !stored.Exists(p => p.Name == name))
                    {
                        shell.WriteError(Loc.GetString("cmd-wg_shippreset-full", ("vessel", vessel), ("max", WGShipConsoleSystem.MaxPresets)));
                        return;
                    }

                    await _presets.Save(grid, player.UserId, name);
                    shell.WriteLine(Loc.GetString("cmd-wg_shippreset-saved", ("name", name), ("vessel", vessel)));
                    break;
                case "load":
                    var data = await _presets.Fetch(player.UserId, vessel, name);
                    if (data == null || !EntityManager.EntityExists(grid))
                    {
                        shell.WriteError(Loc.GetString("cmd-wg_shippreset-missing", ("name", name)));
                        return;
                    }

                    _presets.Apply(grid, data, player.AttachedEntity, name);
                    shell.WriteLine(Loc.GetString("cmd-wg_shippreset-loaded", ("name", name)));
                    break;
                case "delete":
                    shell.WriteLine(await _db.DeleteShipCommandPresetAsync(player.UserId.UserId, vessel, name)
                        ? Loc.GetString("cmd-wg_shippreset-deleted", ("name", name))
                        : Loc.GetString("cmd-wg_shippreset-missing", ("name", name)));
                    break;
            }
        }
        catch (Exception e)
        {
            _log.GetSawmill("wg.shipcommand").Error($"Ship preset command failed: {e}");
            shell.WriteError(Loc.GetString("cmd-wg_shippreset-failed"));
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(new[] { "list", "save", "load", "delete" }, Loc.GetString("cmd-wg_shippreset-hint-sub")),
            2 => CompletionResult.FromHintOptions(new[] { "here" }, Loc.GetString("cmd-wg_ship-hint-grid")),
            3 when args[0] != "list" => CompletionResult.FromHint(Loc.GetString("cmd-wg_shippreset-hint-name")),
            _ => CompletionResult.Empty,
        };
    }
}
