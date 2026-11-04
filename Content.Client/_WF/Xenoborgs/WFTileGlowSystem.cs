using Content.Shared._WF.Xenoborgs;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WF.Xenoborgs;

/// <summary>Resolves the glowing tiles and keeps their overlay up.</summary>
public sealed partial class WFTileGlowSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>Glows by tile type id.</summary>
    public readonly Dictionary<int, (WFTileGlowPrototype Proto, Texture Texture)> Glows = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        Load();
        _overlays.AddOverlay(new WFTileGlowOverlay(EntityManager, _overlays, _prototypes, _timing, this));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WFTileGlowOverlay>();
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WFTileGlowPrototype>())
            Load();
    }

    private void Load()
    {
        Glows.Clear();
        foreach (var proto in _prototypes.EnumeratePrototypes<WFTileGlowPrototype>())
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
