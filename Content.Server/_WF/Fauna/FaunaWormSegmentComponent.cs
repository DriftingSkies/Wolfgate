namespace Content.Server._WF.Fauna;

/// <summary>
/// A body segment of a <see cref="FaunaWormComponent"/> worm.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaWormSegmentComponent : Component
{
    /// <summary>
    /// The worm head this segment follows.
    /// </summary>
    [ViewVariables]
    public EntityUid? Head;
}
