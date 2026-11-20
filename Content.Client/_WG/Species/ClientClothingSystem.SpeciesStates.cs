using Content.Shared.Clothing.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Client.Clothing;

public sealed partial class ClientClothingSystem
{
    /// <summary>
    /// Whether every layer of a species-keyed clothing visual has its state in its sheet. Shared bases list species
    /// states many sheets lack; a species with a missing one draws the plain layers instead of an error sprite.
    /// </summary>
    public bool SpeciesStatesExist(EntityUid uid, ClothingComponent clothing, List<PrototypeLayerData> layers)
    {
        foreach (var layer in layers)
        {
            if (layer.State == null || layer.TexturePath != null)
                continue;

            RSI? rsi = null;
            var path = layer.RsiPath ?? clothing.RsiPath;
            if (path != null)
            {
                if (_cache.TryGetResource<RSIResource>(SpriteSpecifierSerializer.TextureRoot / path, out var res))
                    rsi = res.RSI;
            }
            else if (TryComp(uid, out SpriteComponent? sprite))
                rsi = sprite.BaseRSI;

            if (rsi == null || !rsi.TryGetState(layer.State, out _))
                return false;
        }

        return true;
    }
}
