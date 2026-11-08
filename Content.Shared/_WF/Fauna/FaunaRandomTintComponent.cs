using Robust.Shared.GameStates;

namespace Content.Shared._WF.Fauna;

/// <summary>
/// Tints sprite layers with colours picked at random from palettes when the mob spawns.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class FaunaRandomTintComponent : Component
{
    /// <summary>
    /// Colours to pick from, by palette name.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<string, List<Color>> Palettes = new();

    /// <summary>
    /// The palette each sprite layer takes its colour from, by layer map key. Layers can share a palette.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<string, string> Layers = new();

    /// <summary>
    /// Palette whose pick also colours the mob's point light.
    /// </summary>
    [DataField]
    public string? LightPalette;

    /// <summary>
    /// Colour picked from each palette, by palette name.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<string, Color> Selected = new();
}
