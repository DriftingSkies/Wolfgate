namespace Content.Server._WG.Warheads.Components;

/// <summary>
/// Two different people with access must turn their keys within <see cref="Window"/> seconds to start the countdown.
/// </summary>
[RegisterComponent]
public sealed partial class WGTwoPersonArmingComponent : Component
{
    [DataField]
    public float Window = 3f;

    [DataField]
    public EntityUid? FirstKey;

    [DataField]
    public TimeSpan FirstKeyTime;
}
