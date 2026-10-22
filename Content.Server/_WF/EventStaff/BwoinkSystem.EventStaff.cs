using System.Linq;
using Content.Server._WF.EventStaff;
using Content.Shared._WF.EventStaff;
using Content.Shared.Administration;
using Robust.Shared.Network;

namespace Content.Server.Administration.Systems;

public sealed partial class BwoinkSystem
{
    /// <summary>
    /// The category of each player's current ahelp, set by the player's latest message.
    /// </summary>
    private readonly Dictionary<NetUserId, AHelpCategory> _ahelpCategories = new();

    /// <summary>
    /// The category of a player's ahelp; Player until they pick one.
    /// </summary>
    public AHelpCategory GetAHelpCategory(NetUserId user)
    {
        return _ahelpCategories.GetValueOrDefault(user, AHelpCategory.Player);
    }

    /// <summary>
    /// Whether event staff may answer this player's ahelp: a gameplay one, or none yet.
    /// </summary>
    private bool CanEventStaffAnswer(AdminData? admin, NetUserId channel)
    {
        if (admin is not { Active: true } || !admin.HasFlag(AdminFlags.Event))
            return false;

        AHelpCategory? category = _ahelpCategories.TryGetValue(channel, out var stored) ? stored : null;
        return EventStaffAHelpRules.CanAnswer(AdminFlags.Event, category);
    }

    /// <summary>
    /// Admins who receive ahelps of this category: Adminhelp always, Event for gameplay ones.
    /// </summary>
    private IList<INetChannel> GetTargetAdmins(AHelpCategory category)
    {
        return _adminManager.ActiveAdmins
            .Where(p => _adminManager.GetAdminData(p) is { Active: true } data &&
                        EventStaffAHelpRules.Receives(data.Flags, category))
            .Select(p => p.Channel)
            .ToList();
    }

    /// <summary>
    /// The tag put in front of ahelp lines so staff can tell the categories apart.
    /// </summary>
    private string CategoryTag(AHelpCategory category)
    {
        return Loc.GetString(category == AHelpCategory.Gameplay
            ? "wf-bwoink-category-gameplay"
            : "wf-bwoink-category-player");
    }
}
