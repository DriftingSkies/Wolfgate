namespace Content.Client._WG.Modsuits;

[RegisterComponent]
public sealed partial class ModSuitSealedVisualsComponent : Component
{
    [DataField]
    public string SpriteLayer = "sealed";

    [DataField]
    public Dictionary<string, List<PrototypeLayerData>> ClothingVisuals = new();
}
