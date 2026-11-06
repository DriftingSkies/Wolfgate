using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Sends a chaser after the target that steps toward it tile by tile, leaving an effect on each tile.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaChaserComponent : Component
{
    /// <summary>
    /// Chaser spawned on the mob and sent at the target.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Chaser;
}

/// <summary>
/// A chaser in flight. Picks a new cardinal step toward its target every few steps.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaChaserMoverComponent : Component
{
    /// <summary>
    /// Spawned on each tile the chaser steps onto.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Effect;

    /// <summary>
    /// Time between steps.
    /// </summary>
    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromSeconds(0.3);

    /// <summary>
    /// How long the chaser lasts before it is deleted.
    /// </summary>
    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(9.8);

    /// <summary>
    /// Steps between picks of a new direction toward the target.
    /// </summary>
    [DataField]
    public int RepickEvery = 4;

    /// <summary>
    /// Entity the chaser steps toward.
    /// </summary>
    [ViewVariables]
    public EntityUid? Target;

    /// <summary>
    /// When the next step happens.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan NextStep;

    /// <summary>
    /// When the chaser is deleted.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan EndAt;

    /// <summary>
    /// Steps taken so far.
    /// </summary>
    [ViewVariables]
    public int Steps;

    /// <summary>
    /// Current cardinal step, zero when it must pick a new one.
    /// </summary>
    [ViewVariables]
    public Vector2i Direction;
}
