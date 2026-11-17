using System.Linq;
using Content.Shared.Administration;

namespace Content.Server.Administration.Managers;

public sealed partial class AdminManager
{
    /// <summary>
    /// The Toolshed commands these admin flags allow, or the open ones when <paramref name="data"/> is null. This only
    /// lets the client send them; the server still checks every command in a pipeline.
    /// </summary>
    public List<string> WGToolshedCommandsFor(AdminData? data)
    {
        var commands = new List<string>();
        foreach (var spec in _toolshed.DefaultEnvironment.AllCommands())
        {
            if (!TryGetCommandFlags(spec, out var flags))
                continue;

            if (flags == null || data != null && flags.Any(f => data.HasFlag(f)))
                commands.Add(spec.FullName());
        }

        return commands;
    }
}
