namespace Content.Server._WG.Cyborgs;

/// <summary>Remembers the AI that last took control of this borg, so that AI can reach it off its core's grid.</summary>
[RegisterComponent]
public sealed partial class WGAiRemoteLinkComponent : Component
{
    /// <summary>The linked AI's brain.</summary>
    [DataField]
    public EntityUid? Ai;
}
