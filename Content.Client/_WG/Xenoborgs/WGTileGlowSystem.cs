using Content.Shared._WG.Xenoborgs;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.Xenoborgs;

/// <summary>Resolves the glowing tiles and keeps their overlay up.</summary>
public sealed partial class WGTileGlowSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Glows by tile type id.</summary>
    public readonly Dictionary<int, (WGTileGlowPrototype Proto, Texture Texture)> Glows = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        Load();
        _overlays.AddOverlay(new WGTileGlowOverlay(EntityManager, _overlays, _prototypes, _timing, this));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WGTileGlowOverlay>();
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WGTileGlowPrototype>())
            Load();
    }

    private void Load()
    {
        Glows.Clear();
        foreach (var proto in _prototypes.EnumeratePrototypes<WGTileGlowPrototype>())
        {
            if (!_tileDefs.TryGetDefinition(proto.Tile, out var tile))
            {
                Log.Error($"Tile glow {proto.ID} names unknown tile {proto.Tile}");
                continue;
            }

            Glows[tile.TileId] = (proto, _resources.GetResource<TextureResource>(proto.Sprite).Texture);
        }
    }
}
