using Robust.Shared.Serialization;

namespace Content.Shared._WF.EventStaff;

/// <summary>
/// What an ahelp is about; decides which staff receive it.
/// </summary>
[Serializable, NetSerializable]
public enum AHelpCategory : byte
{
    /// <summary>Reports about other players. Admins only.</summary>
    Player,

    /// <summary>Bugs, stuck players and other gameplay problems. Admins and event staff.</summary>
    Gameplay,
}
