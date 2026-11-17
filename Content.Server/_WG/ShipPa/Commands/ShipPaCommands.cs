using System.Linq;
using Content.Server.Administration;
using Content.Shared._WG.ShipPa;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.ShipPa.Commands;

/// <summary>
/// Shared argument handling for the shippa_* commands.
/// </summary>
public abstract partial class ShipPaCommand : LocalizedEntityCommands
{
    /// <summary>
    /// Resolves a grid from an in-game entity id, complaining to the shell if it isn't one.
    /// </summary>
    protected bool TryGetGrid(IConsoleShell shell, string arg, out EntityUid grid)
    {
        grid = default;

        if (!NetEntity.TryParse(arg, out var net)
            || !EntityManager.TryGetEntity(net, out var uid)
            || !EntityManager.HasComponent<MapGridComponent>(uid))
        {
            shell.WriteError(Loc.GetString("cmd-shippa-invalid-grid", ("grid", arg)));
            return false;
        }

        grid = uid.Value;
        return true;
    }
}

/// <summary>Reads a line out over a ship's PA.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ShipPaAnnounceCommand : ShipPaCommand
{
    [Dependency] private ShipPaSystem _pa = default!;

    public override string Command => "shippa_announce";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!TryGetGrid(shell, args[0], out var grid))
            return;

        var text = string.Join(' ', args.Skip(1));

        if (!_pa.Announce(grid, text))
            shell.WriteError(Loc.GetString("cmd-shippa-no-speakers"));
    }
}

/// <summary>Sets a ship's situation code.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ShipPaCodeCommand : ShipPaCommand
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ShipAlertSystem _alert = default!;

    public override string Command => "shippa_code";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!TryGetGrid(shell, args[0], out var grid))
            return;

        if (!_proto.HasIndex<ShipAlertCodePrototype>(args[1]))
        {
            shell.WriteError(Loc.GetString("cmd-shippa-invalid-code", ("code", args[1])));
            return;
        }

        _alert.SetCode(grid, args[1]);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 2)
            return CompletionResult.Empty;

        var codes = _proto.EnumeratePrototypes<ShipAlertCodePrototype>()
            .OrderBy(proto => proto.Order)
            .Select(proto => new CompletionOption(proto.ID));

        return CompletionResult.FromHintOptions(codes, Loc.GetString("cmd-shippa-hint-code"));
    }
}

/// <summary>Sounds or secures from general quarters on a ship.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ShipPaGeneralQuartersCommand : ShipPaCommand
{
    [Dependency] private ShipAlertSystem _alert = default!;

    public override string Command => "shippa_gq";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !bool.TryParse(args[1], out var active))
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!TryGetGrid(shell, args[0], out var grid))
            return;

        _alert.SetGeneralQuarters(grid, active);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 2
            ? CompletionResult.FromHintOptions(CompletionHelper.Booleans, Loc.GetString("cmd-shippa-hint-active"))
            : CompletionResult.Empty;
    }
}

/// <summary>Lists a ship's speakers and what shape they're in.</summary>
[AdminCommand(AdminFlags.Fun)]
public sealed partial class ShipPaSpeakersCommand : ShipPaCommand
{
    [Dependency] private ShipPaSystem _pa = default!;

    public override string Command => "shippa_speakers";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-wrong-arguments-number"));
            return;
        }

        if (!TryGetGrid(shell, args[0], out var grid))
            return;

        foreach (var speaker in _pa.GetSpeakers(grid))
        {
            shell.WriteLine(
                $"{EntityManager.ToPrettyString(speaker.Owner)} functional={_pa.IsFunctional(speaker)} distortion={speaker.Comp.Distortion:F2} broken={speaker.Comp.Broken}");
        }

        var (online, total, _) = _pa.CountSpeakers(grid);
        shell.WriteLine(Loc.GetString("cmd-shippa_speakers-online", ("online", online), ("total", total)));
    }
}
