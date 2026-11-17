namespace Content.Server._WG.BoardingAction.Components;

/// <summary>On a ship that can't jump while anything here holds it.</summary>
[RegisterComponent]
public sealed partial class WGFtlDisruptedComponent : Component
{
    /// <summary>What holds the ship.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Sources = new();
}
