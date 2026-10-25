using System.Numerics;

namespace Content.Shared._WF.Waddle;

/// <summary>
/// Makes the wearer of these shoes waddle. Visual only, driven by each client from the wearer's move input.
/// </summary>
[RegisterComponent]
public sealed partial class WaddleWhenWornComponent : Component
{
    /// <summary>Sprite offset at the top of each step, in tiles.</summary>
    [DataField]
    public Vector2 HopIntensity = new(0, 0.25f);

    /// <summary>Tilt at the middle of each step, in degrees. The side alternates each step.</summary>
    [DataField]
    public float TumbleIntensity = 20f;

    /// <summary>Seconds one step takes when walking.</summary>
    [DataField]
    public float AnimationLength = 0.66f;

    /// <summary>Factor applied to <see cref="AnimationLength"/> when sprinting.</summary>
    [DataField]
    public float RunAnimationLengthMultiplier = 0.568f;

    /// <summary>Mob the step is added to.</summary>
    [ViewVariables]
    public EntityUid? Wearer;

    /// <summary>Progress through the current step, from 0 to 1.</summary>
    [ViewVariables]
    public float Phase;

    /// <summary>Whether the current step tilts to the negative side.</summary>
    [ViewVariables]
    public bool TiltLeft;

    /// <summary>Rotation added to the wearer's sprite this frame.</summary>
    [ViewVariables]
    public Angle AppliedTilt;

    /// <summary>Offset added to the wearer's sprite this frame.</summary>
    [ViewVariables]
    public Vector2 AppliedHop;

    /// <summary>Sprite rotation left after adding <see cref="AppliedTilt"/>.</summary>
    [ViewVariables]
    public Angle WrittenRotation;

    /// <summary>Sprite offset left after adding <see cref="AppliedHop"/>.</summary>
    [ViewVariables]
    public Vector2 WrittenOffset;
}
