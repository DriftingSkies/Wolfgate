using Robust.Shared.GameStates;

namespace Content.Shared._WF.Warheads;

/// <summary>
/// Floats a whole-second countdown over the entity, after Skyrat's nuke countdown.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WFCountdownDisplayComponent : Component
{
    /// <summary>When the countdown reaches zero; null hides it.</summary>
    [DataField, AutoNetworkedField]
    public TimeSpan? End;

    [DataField, AutoNetworkedField]
    public Color Color = Color.FromHex("#81FF14");
}
