using Content.Shared._WG.Xenonids.ForTheHive;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._WG.Xenonids.ForTheHive;

public sealed partial class XenoForTheHiveVisualizerSystem : VisualizerSystem<ForTheHiveComponent>
{
    [Dependency] private SpriteSystem _sprite = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForTheHiveComponent, ForTheHiveActivatedEvent>(OnForTheHiveAdded);
        SubscribeLocalEvent<ForTheHiveComponent, ForTheHiveCancelledEvent>(OnForTheHiveRemoved);
    }

    private void OnForTheHiveAdded(Entity<ForTheHiveComponent> xeno, ref ForTheHiveActivatedEvent args)
    {
        if (!TryComp<SpriteComponent>(xeno, out var sprite))
            return;

        if (!_sprite.LayerMapTryGet((xeno, sprite), ForTheHiveVisualLayers.Base, out var layer, false) ||
            !_sprite.TryGetLayer((xeno, sprite), layer, out var spriteLayer, false))
            return;

        if (xeno.Comp.ActiveSprite != null)
        {
            _sprite.SetAutoAnimateSync(sprite, spriteLayer, 0);
            _sprite.LayerSetRsi((xeno, sprite), layer, new ResPath(xeno.Comp.ActiveSprite));
        }
    }

    private void OnForTheHiveRemoved(Entity<ForTheHiveComponent> xeno, ref ForTheHiveCancelledEvent args)
    {
        if (!TryComp<SpriteComponent>(xeno, out var sprite))
            return;

        if (!_sprite.LayerMapTryGet((xeno, sprite), ForTheHiveVisualLayers.Base, out var layer, false) ||
            !_sprite.TryGetLayer((xeno, sprite), layer, out var spriteLayer, false))
            return;

        if (xeno.Comp.BaseSprite != null)
        {
            _sprite.SetAutoAnimateSync(sprite, spriteLayer, 0);
            _sprite.LayerSetRsi((xeno, sprite), layer, new ResPath(xeno.Comp.BaseSprite));
        }
    }

    protected override void OnAppearanceChange(EntityUid xeno, ForTheHiveComponent component, ref AppearanceChangeEvent args)
    {
        // TODO RMC14 make the animation speed up overtime
    }
}
