using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.MappingMode.Commands;

/// <summary>Shared helpers for the mapping mode admin commands.</summary>
public abstract partial class WFMappingModeCommandBase : LocalizedEntityCommands
{
    [Dependency] protected IAdminLogManager AdminLog = default!;
    [Dependency] protected WFMappingModeSystem Mapping = default!;

    /// <summary>The live session with a code, or false with the reason written to the shell.</summary>
    protected bool TryGetSession(IConsoleShell shell, string code, [NotNullWhen(true)] out WFMappingSession? session)
    {
        if (Mapping.TryGetSessionByCode(code, out session))
            return true;

        shell.WriteError(Loc.GetString("cmd-wf_mappingmode-no-session", ("code", code)));
        return false;
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(Mapping.Sessions.Select(s => s.Code).Order(),
                Loc.GetString("cmd-wf_mappingmode-hint-code"))
            : CompletionResult.Empty;
    }
}

/// <summary>Lists live sessions: codes, names, owners, members, maps, dirty state and last save.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class WFMappingModeListCommand : WFMappingModeCommandBase
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override string Command => "wf_mappingmode_list";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (Mapping.Sessions.Count == 0)
        {
            shell.WriteLine(Loc.GetString("cmd-wf_mappingmode_list-none"));
            return;
        }

        foreach (var session in Mapping.Sessions.OrderBy(s => s.Id))
        {
            var members = string.Join(", ", session.Members.Keys.Select(user =>
            {
                var name = _players.TryGetPlayerData(user, out var data) ? data.UserName : user.ToString();
                return session.Present.ContainsKey(user) ? name : Loc.GetString("cmd-wf_mappingmode_list-absent", ("name", name));
            }));

            shell.WriteLine(Loc.GetString("cmd-wf_mappingmode_list-entry",
                ("code", session.Code),
                ("name", session.Name),
                ("owner", _players.TryGetPlayerData(session.Owner, out var owner) ? owner.UserName : session.Owner.ToString()),
                ("members", members),
                ("maps", Mapping.SessionMaps(session.Id).Count),
                ("dirty", session.Dirty ? "yes" : "no"),
                ("saved", session.LastSaved?.ToString("u") ?? "-")));

            if (session.Test is { } test)
            {
                shell.WriteLine(Loc.GetString("cmd-wf_mappingmode_list-testing",
                    ("minutes", (int) (_timing.CurTime - test.Started).TotalMinutes),
                    ("testers", string.Join(", ", test.Testers.Keys.Select(user =>
                        _players.TryGetPlayerData(user, out var data) ? data.UserName : user.ToString())))));
            }
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.Empty;
    }
}

/// <summary>Takes the caller's entity to the origin of a session's ground level.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class WFMappingModeTeleportCommand : WFMappingModeCommandBase
{
    [Dependency] private SharedTransformSystem _transform = default!;

    public override string Command => "wf_mappingmode_tp";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (shell.Player?.AttachedEntity is not { } entity)
        {
            shell.WriteError(Loc.GetString("cmd-wf_mappingmode_tp-no-entity"));
            return;
        }

        if (!TryGetSession(shell, args[0], out var session))
            return;

        _transform.SetMapCoordinates(entity, new MapCoordinates(Vector2.Zero, Mapping.RootMapId(session)));
        AdminLog.Add(LogType.Action, LogImpact.Low,
            $"{shell.Player:player} teleported to mapping session {session.Code}");
    }
}

/// <summary>Autosaves a session to its owner's library, then closes it.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class WFMappingModeCloseCommand : WFMappingModeCommandBase
{
    public override string Command => "wf_mappingmode_close";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryGetSession(shell, args[0], out var session))
            return;

        Mapping.CloseSession(session, autosave: true);
        AdminLog.Add(LogType.Action, LogImpact.Medium,
            $"{shell.Player:player} closed mapping session {session.Code} \"{session.Name}\"");
        shell.WriteLine(Loc.GetString("cmd-wf_mappingmode_close-closed", ("code", session.Code)));
    }
}

/// <summary>Removes a player from their session and returns them to the lobby.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class WFMappingModeKickCommand : WFMappingModeCommandBase
{
    [Dependency] private IPlayerManager _players = default!;

    public override string Command => "wf_mappingmode_kick";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!_players.TryGetPlayerDataByUsername(args[0], out var data))
        {
            shell.WriteError(Loc.GetString("cmd-wf_mappingmode_kick-no-player", ("name", args[0])));
            return;
        }

        if (!Mapping.TryGetMembership(data.UserId, out var session))
        {
            shell.WriteError(Loc.GetString("cmd-wf_mappingmode_kick-not-member", ("name", data.UserName)));
            return;
        }

        Mapping.LeaveSession(data.UserId, kicked: true);
        AdminLog.Add(LogType.Action, LogImpact.Medium,
            $"{shell.Player:player} kicked {data.UserName} from mapping session {session.Code}");
        shell.WriteLine(Loc.GetString("cmd-wf_mappingmode_kick-kicked", ("name", data.UserName), ("code", session.Code)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(players: _players),
                Loc.GetString("cmd-wf_mappingmode-hint-player"))
            : CompletionResult.Empty;
    }
}

/// <summary>Saves a session to its owner's library now.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class WFMappingModeSaveCommand : WFMappingModeCommandBase
{
    [Dependency] private WFMappingLibrarySystem _library = default!;

    public override string Command => "wf_mappingmode_save";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryGetSession(shell, args[0], out var session))
            return;

        var saved = _library.Autosave(session);
        AdminLog.Add(LogType.Action, LogImpact.Low,
            $"{shell.Player:player} saved mapping session {session.Code}: {(saved ? "written" : "failed")}");
        shell.WriteLine(Loc.GetString(saved ? "cmd-wf_mappingmode_save-saved" : "cmd-wf_mappingmode_save-failed",
            ("code", session.Code)));
    }
}

/// <summary>Deletes every entity of one prototype on a session's maps, for cleaning up what slipped past the limits.</summary>
[AdminCommand(AdminFlags.Mapping)]
public sealed partial class WFMappingModePurgeCommand : WFMappingModeCommandBase
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override string Command => "wf_mappingmode_purge";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteLine(Help);
            return;
        }

        if (!TryGetSession(shell, args[0], out var session))
            return;

        if (!_prototypes.HasIndex<EntityPrototype>(args[1]))
        {
            shell.WriteError(Loc.GetString("cmd-wf_mappingmode_purge-no-prototype", ("prototype", args[1])));
            return;
        }

        var count = Mapping.Purge(session, args[1]);
        AdminLog.Add(LogType.Action, LogImpact.Medium,
            $"{shell.Player:player} purged {count} {args[1]} from mapping session {session.Code} \"{session.Name}\"");
        shell.WriteLine(Loc.GetString("cmd-wf_mappingmode_purge-purged", ("count", count), ("prototype", args[1]),
            ("code", session.Code)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => base.GetCompletion(shell, args),
            2 => CompletionResult.FromHintOptions(CompletionHelper.PrototypeIDs<EntityPrototype>(),
                Loc.GetString("cmd-wf_mappingmode-hint-prototype")),
            _ => CompletionResult.Empty,
        };
    }
}
