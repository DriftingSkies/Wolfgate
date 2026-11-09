using System.Linq;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._WF.Photography;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Containers;

namespace Content.Server._WF.Photography;

/// <summary>Deletes a photo's image for good, by hash or by a photo entity, and blanks every copy of it.</summary>
[AdminCommand(AdminFlags.Moderator)]
public sealed partial class BlankPhotoCommand : LocalizedEntityCommands
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private PhotoStoreSystem _store = default!;

    public override string Command => "blankphoto";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("cmd-blankphoto-invalid-args"));
            shell.WriteLine(Help);
            return;
        }

        if (ResolveHash(args[0]) is not { } hash)
        {
            shell.WriteError(Loc.GetString("cmd-blankphoto-bad-target", ("target", args[0])));
            return;
        }

        var blanked = _store.Blank(hash);
        _adminLog.Add(LogType.WFPhoto, LogImpact.Medium,
            $"{shell.Player?.Name ?? "Server"} blanked photo {hash}, {blanked} copies in the round");
        shell.WriteLine(Loc.GetString("cmd-blankphoto-success", ("hash", hash), ("count", blanked)));
    }

    /// <summary>A 64-character hash as given, or the hash of the photo or framed photo an entity ID points at.</summary>
    private string? ResolveHash(string target)
    {
        if (target.Length == 64 && target.All(Uri.IsHexDigit))
            return target.ToUpperInvariant();

        if (!NetEntity.TryParse(target, out var net) || !EntityManager.TryGetEntity(net, out var uid))
            return null;

        if (EntityManager.TryGetComponent<PhotoComponent>(uid, out var photo))
            return photo.Hash.Length > 0 ? photo.Hash : null;

        var containers = EntityManager.System<SharedContainerSystem>();
        if (containers.TryGetContainer(uid.Value, PhotoFrameComponent.ContainerId, out var frame)
            && frame.ContainedEntities.Count > 0
            && EntityManager.TryGetComponent(frame.ContainedEntities[0], out photo)
            && photo.Hash.Length > 0)
            return photo.Hash;

        return null;
    }
}
