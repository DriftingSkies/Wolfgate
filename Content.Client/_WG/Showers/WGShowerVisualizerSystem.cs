using Content.Shared._WG.Showers;
using Robust.Client.GameObjects;

namespace Content.Client._WG.Showers;

/// <summary>Shows the falling water, tinted by what the tank holds, and the steam.</summary>
public sealed class WGShowerVisualizerSystem : VisualizerSystem<WGShowerComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, WGShowerComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite)
            return;

        var on = AppearanceSystem.TryGetData<bool>(uid, WGShowerVisuals.On, out var isOn, args.Component) && isOn;
        var mist = AppearanceSystem.TryGetData<bool>(uid, WGShowerVisuals.Mist, out var hasMist, args.Component) && hasMist;
        var ent = (uid, sprite);

        SpriteSystem.LayerSetVisible(ent, WGShowerVisualLayers.Water, on);
        SpriteSystem.LayerSetVisible(ent, WGShowerVisualLayers.Mist, mist);

        if (!AppearanceSystem.TryGetData<Color>(uid, WGShowerVisuals.Color, out var color, args.Component))
            return;

        // The sprite is already water-blue, so pull the tint halfway to white to keep plain water looking clear.
        var tint = Color.InterpolateBetween(Color.White, color.WithAlpha(1f), 0.6f);
        SpriteSystem.LayerSetColor(ent, WGShowerVisualLayers.Water, tint);
        SpriteSystem.LayerSetColor(ent, WGShowerVisualLayers.Mist, tint.WithAlpha(0.8f));
    }
}
