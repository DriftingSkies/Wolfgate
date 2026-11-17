using Content.Server._WG.Audio.InternetSound;
using Content.Server.Administration;
using Content.Shared._WG.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Administration.Commands;

/// <summary>
/// Plays a YouTube, SoundCloud or other yt-dlp supported link to every connected player.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class PlayInternetSoundCommand : LocalizedEntityCommands
{
    [Dependency] private InternetSoundSystem _internetSound = default!;

    public override string Command => WolfgateAdminCommands.PlayInternetSound;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Loc.GetString("cmd-playinternetsound-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (!InternetSoundCommandArgs.TryParseStealth(shell, args, 1, out var stealth))
            return;

        _internetSound.Play(shell.Player, args[0], stealth);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHint(Loc.GetString("cmd-playinternetsound-hint")),
            2 => CompletionResult.FromHintOptions(CompletionHelper.Booleans, Loc.GetString("cmd-playinternetsound-hint-stealth")),
            _ => CompletionResult.Empty,
        };
    }
}

/// <summary>
/// Plays a link out of one ship's PA speakers, where it carries, fades and distorts like anything else the
/// PA plays rather than being piped straight into everyone's ears.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class PlayInternetSoundPaCommand : LocalizedEntityCommands
{
    [Dependency] private InternetSoundSystem _internetSound = default!;

    public override string Command => WolfgateAdminCommands.PlayInternetSoundPa;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteError(Loc.GetString("cmd-playinternetsoundpa-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (!NetEntity.TryParse(args[0], out var net)
            || !EntityManager.TryGetEntity(net, out var grid)
            || !EntityManager.HasComponent<MapGridComponent>(grid))
        {
            shell.WriteError(Loc.GetString("cmd-playinternetsoundpa-invalid-grid", ("grid", args[0])));
            return;
        }

        if (!InternetSoundCommandArgs.TryParseStealth(shell, args, 2, out var stealth))
            return;

        _internetSound.PlayOverPa(shell.Player, args[1], grid.Value, stealth);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<MapGridComponent>(args[0], EntityManager),
                Loc.GetString("cmd-playinternetsoundpa-hint-grid")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-playinternetsound-hint")),
            3 => CompletionResult.FromHintOptions(CompletionHelper.Booleans, Loc.GetString("cmd-playinternetsound-hint-stealth")),
            _ => CompletionResult.Empty,
        };
    }
}

/// <summary>
/// The optional stealth flag both play commands take.
/// </summary>
internal static class InternetSoundCommandArgs
{
    public static bool TryParseStealth(IConsoleShell shell, string[] args, int index, out bool stealth)
    {
        stealth = false;

        if (args.Length <= index || bool.TryParse(args[index], out stealth))
            return true;

        shell.WriteError(Loc.GetString("cmd-playinternetsound-invalid-stealth", ("value", args[index])));
        return false;
    }
}

/// <summary>
/// Stops the current internet sound for everyone and cancels any fetch in progress.
/// </summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class StopInternetSoundCommand : LocalizedEntityCommands
{
    [Dependency] private InternetSoundSystem _internetSound = default!;

    public override string Command => WolfgateAdminCommands.StopInternetSound;

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _internetSound.Stop(shell.Player);
    }
}
