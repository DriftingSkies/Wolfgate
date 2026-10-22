using Content.Shared._WF.EventStaff;
using Content.Shared.Administration;

namespace Content.Server._WF.EventStaff;

/// <summary>
/// Who receives and who may answer an ahelp, by admin flags and category.
/// </summary>
public static class EventStaffAHelpRules
{
    /// <summary>
    /// Whether staff with these flags receive ahelps of this category.
    /// </summary>
    public static bool Receives(AdminFlags flags, AHelpCategory category)
    {
        return (flags & AdminFlags.Adminhelp) != 0 ||
               category == AHelpCategory.Gameplay && (flags & AdminFlags.Event) != 0;
    }

    /// <summary>
    /// Whether staff with these flags may answer an ahelp; a null category means the player has none yet.
    /// </summary>
    public static bool CanAnswer(AdminFlags flags, AHelpCategory? category)
    {
        return Receives(flags, category ?? AHelpCategory.Gameplay);
    }
}
