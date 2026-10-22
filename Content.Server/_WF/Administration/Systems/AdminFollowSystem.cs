using Content.Shared.Follower;
using Content.Shared.Ghost;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._WF.Administration.Systems;

/// <summary>
/// Admin follow aghosts the admin first, so the ghost orbits the target instead of their body.
/// </summary>
public sealed partial class AdminFollowSystem : EntitySystem
{
    [Dependency] private IConsoleHost _console = default!;
    [Dependency] private FollowerSystem _follower = default!;

    /// <summary>
    /// Aghosts the admin unless they are already a ghost, then orbits the target.
    /// </summary>
    public void Follow(ICommonSession admin, EntityUid target)
    {
        if (admin.AttachedEntity is not { Valid: true } attached)
            return;

        if (!HasComp<GhostComponent>(attached))
            _console.ExecuteCommand(admin, "aghost");

        if (admin.AttachedEntity is not { Valid: true } ghost || ghost == target)
            return;

        _follower.StartFollowingEntity(ghost, target);
    }
}
