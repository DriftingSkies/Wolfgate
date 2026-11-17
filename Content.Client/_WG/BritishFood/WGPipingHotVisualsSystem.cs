using Content.Shared._WG.BritishFood;
using Robust.Client.GameObjects;

namespace Content.Client._WG.BritishFood;

/// <summary>Draws steam over piping hot food until it cools.</summary>
public sealed partial class WGPipingHotVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string SteamKey = "wg-piping-hot-steam";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGPipingHotComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WGPipingHotComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WGPipingHotComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var layer = _sprite.LayerMapReserve((ent, sprite), SteamKey);
        _sprite.LayerSetSprite((ent, sprite), layer, ent.Comp.Steam);
        _sprite.LayerSetVisible((ent, sprite), layer, true);
    }

    private void OnShutdown(Entity<WGPipingHotComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite) &&
            _sprite.LayerMapTryGet((ent, sprite), SteamKey, out var layer, false))
            _sprite.LayerSetVisible((ent, sprite), layer, false);
    }
}
