using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Modsuits.Support;

/// <summary>Gives an entity a waddle animation when it moves.</summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedWaddleAnimationSystem))]
[AutoGenerateComponentState(true, true)]
public sealed partial class WaddleAnimationComponent : Component
{
    [DataField]
    public string KeyName = "Waddle";

    [DataField, AutoNetworkedField]
    public Vector2 HopIntensity = new(0, 0.25f);

    [DataField, AutoNetworkedField]
    public float TumbleIntensity = 20.0f;

    [DataField, AutoNetworkedField]
    public TimeSpan AnimationLength = TimeSpan.FromSeconds(0.66f);

    [DataField, AutoNetworkedField]
    public float RunAnimationLengthMultiplier = 0.568f;

    /// <summary>Stores which step was last played so a restart looks natural. Client only.</summary>
    public bool LastStep;

    [DataField, AutoNetworkedField]
    public bool IsWaddling;
}
