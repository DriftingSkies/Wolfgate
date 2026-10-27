using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Toolshed;
using Robust.Shared.Utility;

namespace Content.Server.Symphony;

/// <summary>
/// Every console command this build has, for the SSymphony panel's staff handbook: the server's own commands, the
/// Toolshed ones, and the client's, each with its description, help and the admin flags it needs, worked out the way
/// the admin manager works them out. Read only, behind the admin API token like /symphony/cvars.
/// </summary>
public sealed partial class SymphonyCommandsSystem : EntitySystem
{
    /// <summary>Status host path: GET lists the commands.</summary>
    public const string CommandsPath = "/symphony/commands";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private ToolshedManager _toolshed = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private ILocalizationManager _loc = default!;

    public override void Initialize()
    {
        base.Initialize();
        _statusHost.AddHandler(HandleAsync);
    }

    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        if (context.Url.AbsolutePath != CommandsPath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;
        if (context.RequestMethod != HttpMethod.Get)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        await context.RespondJsonAsync(await _tasks.OnMainThread(Snapshot));
        return true;
    }

    private CommandsResponse Snapshot()
    {
        // The engine's own commands carry no attributes; their flags come from these files, as in the admin manager.
        var engine = ReadPerms("/engineCommandPerms.yml");
        var toolshedEngine = ReadPerms("/toolshedEngineCommandPerms.yml");
        var client = ReadPerms("/clientCommandPerms.yml");

        var console = new List<CommandEntry>();
        foreach (var (name, cmd) in _console.AvailableCommands)
        {
            var (avail, flags) = RequiredFlags(cmd);
            var entry = new CommandEntry { Name = name, Description = Safe(() => cmd.Description), Help = Named(Safe(() => cmd.Help), name) };
            if (avail)
                Access(entry, flags.Length == 0 ? null : flags);
            else
                Access(entry, engine, name);
            console.Add(entry);
        }

        var toolshed = new List<CommandEntry>();
        foreach (var spec in _toolshed.DefaultEnvironment.AllCommands())
        {
            var (avail, flags) = RequiredFlags(spec.Cmd);
            var entry = new CommandEntry
            {
                Name = spec.FullName(),
                Description = Safe(() => spec.Cmd.Description(spec.SubCommand)),
                Help = Named(Safe(() => spec.Cmd.GetHelp(spec.SubCommand)), spec.FullName()),
            };
            if (avail)
                Access(entry, flags.Length == 0 ? null : flags);
            else
                Access(entry, toolshedEngine, spec.Cmd.Name);
            toolshed.Add(entry);
        }

        // The client's commands live in the client; what the server knows is their names and flags, and the shared locale.
        var clientCmds = new List<CommandEntry>();
        foreach (var name in client.AnyCommands.Concat(client.AdminCommands.Keys).Distinct())
        {
            var entry = new CommandEntry
            {
                Name = name,
                Description = _loc.TryGetString($"cmd-{name}-desc", out var d) ? d : "",
                Help = _loc.TryGetString($"cmd-{name}-help", out var h) ? Named(h, name) : "",
            };
            Access(entry, client, name);
            clientCmds.Add(entry);
        }

        return new CommandsResponse
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Console = console.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
            Toolshed = toolshed.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
            Client = clientCmds.OrderBy(c => c.Name, StringComparer.Ordinal).ToList(),
            Flags = Enum.GetValues<AdminFlags>().Where(f => f != AdminFlags.None && f != AdminFlags.Host && (f & (f - 1)) == 0)
                .Select(f => AdminFlagsHelper.FlagsToNames(f)[0]).ToList(),
        };
    }

    private AdminCommandPermissions ReadPerms(string path)
    {
        var perms = new AdminCommandPermissions();
        if (_res.TryContentFileRead(new ResPath(path), out var fs))
        {
            using (fs)
                perms.LoadPermissionsFromStream(fs);
        }
        return perms;
    }

    // Same reading as AdminManager.GetRequiredFlag: no attribute at all means the command is the server console's alone.
    private static (bool avail, AdminFlags[] flags) RequiredFlags(object cmd)
    {
        MemberInfo type = cmd.GetType();
        if (cmd is ConsoleHost.RegisteredCommand registered)
            type = registered.Callback.Method;
        if (Attribute.IsDefined(type, typeof(AnyCommandAttribute)))
            return (true, Array.Empty<AdminFlags>());
        var attribs = type.GetCustomAttributes(typeof(AdminCommandAttribute)).Cast<AdminCommandAttribute>().Select(p => p.Flags).ToArray();
        return (attribs.Length != 0, attribs);
    }

    private static void Access(CommandEntry entry, AdminFlags[]? flags)
    {
        entry.Access = flags == null ? "anyone" : "admin";
        entry.Flags = flags?.Select(f => AdminFlagsHelper.FlagsToNames(f)).ToList();
    }

    private static void Access(CommandEntry entry, AdminCommandPermissions perms, string name)
    {
        if (perms.AnyCommands.Contains(name))
            Access(entry, null);
        else if (perms.AdminCommands.TryGetValue(name, out var flags))
            Access(entry, flags);
        else
            entry.Access = "host";
    }

    // Help strings written for the console say {$command} where the console fills in the name.
    private static string Named(string help, string name) => help.Replace("{$command}", name);

    // A localized description can throw on a missing string; the list is still worth having without it.
    private static string Safe(Func<string> read)
    {
        try
        {
            return read() ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private sealed class CommandsResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("console")] public List<CommandEntry> Console { get; set; } = new();
        [JsonPropertyName("toolshed")] public List<CommandEntry> Toolshed { get; set; } = new();
        [JsonPropertyName("client")] public List<CommandEntry> Client { get; set; } = new();
        [JsonPropertyName("flags")] public List<string> Flags { get; set; } = new();
    }

    private sealed class CommandEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("help")] public string Help { get; set; } = "";

        /// <summary>anyone, admin (with one of <see cref="Flags"/>) or host (the server console only).</summary>
        [JsonPropertyName("access")] public string Access { get; set; } = "host";

        /// <summary>Any one of these sets of flags is enough; each set is all of its names.</summary>
        [JsonPropertyName("flags")] public List<string[]>? Flags { get; set; }
    }
}
