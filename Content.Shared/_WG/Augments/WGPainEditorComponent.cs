using Robust.Shared.GameStates;

namespace Content.Shared._WG.Augments;

/// <summary>A pain editor in the head: its body gains less pain from every wound.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGPainEditorComponent : Component
{
    /// <summary>What every pain gain is multiplied by.</summary>
    [DataField]
    public float PainMultiplier = 0.6f;
}
