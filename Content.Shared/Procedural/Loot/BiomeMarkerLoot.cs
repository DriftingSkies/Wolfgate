using Content.Shared.Parallax.Biomes.Markers;
using Content.Shared._WF.EngineCompat;

namespace Content.Shared.Procedural.Loot;

/// <summary>
/// Adds a biome marker layer for dungeon loot.
/// </summary>
public sealed partial class BiomeMarkerLoot : IDungeonLoot
{
    [DataField("proto", required: true,
        customTypeSerializer: typeof(PrototypeIdValueDictionarySerializer<string, BiomeMarkerLayerPrototype>))]
    public Dictionary<string, string> Prototype = new();
}
