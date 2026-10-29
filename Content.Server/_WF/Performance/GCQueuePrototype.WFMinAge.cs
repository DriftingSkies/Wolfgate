namespace Content.Server.Worldgen.Prototypes;

public sealed partial class GCQueuePrototype
{
    /// <summary>
    /// How long an entity waits in the queue before it can be deleted, so a chunk that loads again soon keeps its
    /// debris instead of deleting it and spawning new.
    /// </summary>
    [DataField("minAge")]
    public TimeSpan WFMinAge { get; private set; } = TimeSpan.Zero;
}
