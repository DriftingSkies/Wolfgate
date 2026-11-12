using Content.Shared._WF.Hideouts.Blueprint;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Hideouts.Blueprint;

/// <summary>The picture a blueprint brush is shown with: its entity's sprite, its floor tile or its decal.</summary>
public static class WFBlueprintIcons
{
    private static readonly Dictionary<string, Texture?> Cache = new();

    /// <summary>A brush's picture, or null for a brush with none (the roof and removal tools).</summary>
    public static Texture? Get(WFBlueprintEntry brush)
    {
        if (Cache.TryGetValue(brush.Id, out var cached))
            return cached;

        Texture? texture = null;
        try
        {
            var protos = IoCManager.Resolve<IPrototypeManager>();
            var sprites = IoCManager.Resolve<IEntityManager>().System<SpriteSystem>();
            if (brush.Entity is { } entity && protos.HasIndex<EntityPrototype>(entity))
            {
                texture = sprites.GetPrototypeIcon(entity).Default;
            }
            else if (brush.Decal is { } decal && protos.TryIndex<DecalPrototype>(decal, out var decalProto))
            {
                texture = sprites.Frame0(decalProto.Sprite);
            }
            else if (brush.Tile is { } tile
                     && IoCManager.Resolve<ITileDefinitionManager>().TryGetDefinition(tile, out var def)
                     && def is ContentTileDefinition { Sprite: { } path }
                     && IoCManager.Resolve<IResourceCache>().TryGetResource<TextureResource>(path, out var resource))
            {
                // A tile's sheet holds its variants side by side; the first is its picture.
                var side = Math.Min(resource.Texture.Width, resource.Texture.Height);
                texture = new AtlasTexture(resource.Texture, UIBox2.FromDimensions(0, 0, side, side));
            }
        }
        catch (Exception)
        {
            // A prototype with no usable sprite just has no picture.
        }

        Cache[brush.Id] = texture;
        return texture;
    }
}
