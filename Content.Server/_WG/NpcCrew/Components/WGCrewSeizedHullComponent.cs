namespace Content.Server._WG.NpcCrew.Components;

/// <summary>
/// On an NPC ship's grid that boarders hold: its colours no longer speak for it, so no crew takes it for a friend,
/// a formation partner or a ship that may dock.
/// </summary>
[RegisterComponent]
public sealed partial class WGCrewSeizedHullComponent : Component
{
    /// <summary>When it was seized.</summary>
    [ViewVariables]
    public TimeSpan Since;
}
