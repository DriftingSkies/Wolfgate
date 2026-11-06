namespace Content.Server._WF.Fauna;

/// <summary>
/// Says a random line now and then, like a source mob's speak list.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaIdleSpeechComponent : Component
{
    /// <summary>
    /// Lines picked from at random.
    /// </summary>
    [DataField(required: true)]
    public List<LocId> Lines = new();

    /// <summary>
    /// How often a line may be said, the source's life tick.
    /// </summary>
    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Chance per interval to say a line, the source's speak chance.
    /// </summary>
    [DataField]
    public float Chance = 0.05f;

    /// <summary>
    /// Stays quiet while the NPC has a target.
    /// </summary>
    [DataField]
    public bool IdleOnly = true;

    /// <summary>
    /// When the next speech roll happens.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextCheck;
}
