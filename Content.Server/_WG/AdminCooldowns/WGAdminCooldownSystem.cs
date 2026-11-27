using Content.Server.Administration.Managers;
using Robust.Shared.Player;

namespace Content.Server._WG.AdminCooldowns;

/// <summary>
/// Tells upstream request handlers whether the requester is an active admin, who skips the cooldowns on player
/// requests (announcements, faxes, news, bounty posts).
/// </summary>
public sealed class WGAdminCooldownSystem : EntitySystem
{
    [Dependency] private IAdminManager _admin = default!;

    /// <summary>Whether the entity is played by an active (not deadminned) admin.</summary>
    public bool Skips(EntityUid? user)
    {
        return user is { } uid && TryComp<ActorComponent>(uid, out var actor) && _admin.IsAdmin(actor.PlayerSession);
    }
}
