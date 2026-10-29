using Content.Shared._WF.BritishFood;
using Robust.Client.GameObjects;

namespace Content.Client._WF.BritishFood;

/// <summary>Draws steam over piping hot food until it cools.</summary>
public sealed partial class WFPipingHotVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    private const string SteamKey = "wf-piping-hot-steam";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPipingHotComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFPipingHotComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<WFPipingHotComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var layer = _sprite.LayerMapReserve((ent, sprite), SteamKey);
        _sprite.LayerSetSprite((ent, sprite), layer, ent.Comp.Steam);
        _sprite.LayerSetVisible((ent, sprite), layer, true);
    }

    private void OnShutdown(Entity<WFPipingHotComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite) &&
            _sprite.LayerMapTryGet((ent, sprite), SteamKey, out var layer, false))
            _sprite.LayerSetVisible((ent, sprite), layer, false);
    }
}
