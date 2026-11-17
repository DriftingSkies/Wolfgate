using Content.Client._WG.Administration.UI.SpawnOutfit;
using Content.Shared._WG.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Client._WG.Administration.Commands;

/// <summary>
/// Opens the Spawn as Outfit picker for a target entity. The admin verb sends this to the client.
/// Anyone may open the picker; the spawn command it runs is admin-gated server-side.
/// </summary>
[AnyCommand]
public sealed class SpawnOutfitUiCommand : LocalizedCommands
{
    public override string Command => WolfgateAdminCommands.SpawnOutfitUi;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !int.TryParse(args[0], out var targetInt))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfitui-invalid-args"));
            return;
        }

        new SpawnOutfitMenu(OutfitMenuMode.Spawn, new NetEntity(targetInt)).OpenCentered();
    }
}
