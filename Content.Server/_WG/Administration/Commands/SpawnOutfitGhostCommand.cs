using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Commands;
using Content.Server.Administration.Logs;
using Content.Server.Mind;
using Content.Server.Preferences.Managers;
using Content.Server.Station.Systems;
using Content.Shared._WG.Administration;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Shared.Console;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Administration.Commands;

/// <summary>
/// Spawns a ghost's player as their selected character wearing a starting gear outfit, at the ghost, and puts them in it.
/// </summary>
[AdminCommand(AdminFlags.Spawn | AdminFlags.Fun)]
public sealed partial class SpawnOutfitGhostCommand : LocalizedEntityCommands
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private MindSystem _mind = default!;

    public override string Command => WolfgateAdminCommands.SpawnOutfitGhost;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfitghost-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (!int.TryParse(args[0], out var ghostInt)
            || !EntityManager.TryGetEntity(new NetEntity(ghostInt), out var ghost)
            || !EntityManager.HasComponent<GhostComponent>(ghost)
            || !EntityManager.TryGetComponent<TransformComponent>(ghost, out var ghostXform))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfitghost-invalid-ghost"));
            return;
        }

        if (!EntityManager.TryGetComponent<ActorComponent>(ghost, out var actor))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfitghost-no-player"));
            return;
        }

        if (!_prototypeManager.TryIndex<StartingGearPrototype>(args[1], out var gear))
        {
            shell.WriteError(Loc.GetString("cmd-spawnoutfit-unknown-gear", ("id", args[1])));
            return;
        }

        // Their own character when it is a humanoid; otherwise a random human rather than nothing.
        var player = actor.PlayerSession;
        var profile = _preferences.GetPreferencesOrNull(player.UserId)?.SelectedCharacter as HumanoidCharacterProfile
                      ?? HumanoidCharacterProfile.RandomWithSpecies();

        var mob = _stationSpawning.SpawnPlayerMob(ghostXform.Coordinates, null, profile, null);
        SetOutfitCommand.SetOutfit(mob, gear.ID, EntityManager);
        _mind.ControlMob(player.UserId, mob);

        _adminLogger.Add(LogType.EntitySpawn, LogImpact.High,
            $"{shell.Player?.Name ?? "Server"} spawned {player.Name} as {EntityManager.ToPrettyString(mob):mob} wearing {gear.ID} at {EntityManager.ToPrettyString(ghost.Value):ghost}");
        shell.WriteLine(Loc.GetString("cmd-spawnoutfitghost-success",
            ("player", player.Name), ("mob", EntityManager.ToPrettyString(mob)), ("gear", gear.ID)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-spawnoutfitghost-hint-ghost")),
            2 => CompletionResult.FromHintOptions(
                _prototypeManager.EnumeratePrototypes<StartingGearPrototype>().Select(gear => gear.ID).OrderBy(id => id),
                Loc.GetString("cmd-spawnoutfit-hint-gear")),
            _ => CompletionResult.Empty,
        };
    }
}
