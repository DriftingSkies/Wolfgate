using Robust.Shared.GameStates;

namespace Content.Shared.Light.Components;

/// <summary>
/// Will draw shadows over tiles flagged as roof tiles on the attached grid. ImplicitRoofComponent will get removed if the grid has this component.
/// </summary>
// WOLFGATE(Performance) START: networked by SharedRoofSystem.WFDelta, which sends only changed chunks
[RegisterComponent, NetworkedComponent]
public sealed partial class RoofComponent : Component
{
    public const int ChunkSize = 8;

    [DataField]
    public Color Color = Color.Black;

    /// <summary>
    /// Chunk origin and bitmask of value in chunk.
    /// </summary>
    [DataField]
    public Dictionary<Vector2i, ulong> Data = new();
// WOLFGATE END
}
