using Content.Server.Administration;
using Content.Shared._WG.Paintings;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._WG.Paintings;

/// <summary>Blocks a catalogue painting by hash, or lifts a block; the hash is in the WGPainting admin log.</summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class WGPaintingBlockCommand : LocalizedEntityCommands
{
    [Dependency] private WGPaintingStoreSystem _store = default!;

    public override string Command => "paintingblock";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var blocked = true;
        if (args.Length is < 1 or > 2
            || !WGPaintingRules.IsValidHash(args[0])
            || args.Length == 2 && !bool.TryParse(args[1], out blocked))
        {
            shell.WriteError(Loc.GetString("cmd-paintingblock-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        var found = await _store.SetBlocked(args[0], blocked, shell.Player?.Name ?? "Server");
        shell.WriteLine(Loc.GetString(!found ? "cmd-paintingblock-unknown"
            : blocked ? "cmd-paintingblock-blocked"
            : "cmd-paintingblock-unblocked"));
    }
}
