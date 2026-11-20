namespace Content.Server._WG.Cyborgs;

/// <summary>
/// On an AI's remote eye while the AI views an overwatch camera feed; moving the eye ends the feed.
/// </summary>
[RegisterComponent]
public sealed partial class WGAiOverwatchEyeComponent : Component
{
    /// <summary>
    /// The AI brain doing the watching.
    /// </summary>
    [ViewVariables]
    public EntityUid Ai;
}
