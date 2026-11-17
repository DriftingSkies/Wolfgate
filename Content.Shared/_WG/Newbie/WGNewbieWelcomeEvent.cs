using Robust.Shared.Serialization;

namespace Content.Shared._WG.Newbie;

/// <summary>
/// Sent once per join to a new player: opens the welcome popup, and the crew popup if <see cref="Fresh"/>.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGNewbieWelcomeEvent : EntityEventArgs
{
    /// <summary>The player is under <see cref="WGNewbieCVars.FreshHours"/> and should find a crew first.</summary>
    public bool Fresh;

    public WGNewbieWelcomeEvent(bool fresh)
    {
        Fresh = fresh;
    }
}
