namespace Content.Server._WF.OutpostRaids;

/// <summary>The raid director's book on an outpost: the attention it has drawn and how far up the ladder it is. Saved with it.</summary>
[RegisterComponent, Access(typeof(WFRaidDirectorSystem))]
public sealed partial class WFRaidAttentionComponent : Component
{
    /// <summary>Threat points built up, spent on raids.</summary>
    [DataField]
    public float Attention;

    /// <summary>The highest ladder step a raid on this outpost may be: one past the last it beat.</summary>
    [DataField]
    public int Step;

    /// <summary>No raid before this: the grace after founding or loading.</summary>
    public TimeSpan GraceUntil;

    /// <summary>No raid before this either: the end of the window it was last raided in.</summary>
    public TimeSpan RaidedUntil;

    /// <summary>Whether the grace has been set since it was founded or loaded.</summary>
    public bool Started;
}
