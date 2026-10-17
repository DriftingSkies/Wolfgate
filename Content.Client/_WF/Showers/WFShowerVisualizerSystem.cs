using Content.Shared._WF.Showers;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Showers;

/// <summary>Shows the falling water, tinted by what the tank holds, and the steam.</summary>
public sealed class WFShowerVisualizerSystem : VisualizerSystem<WFShowerComponent>
{
    protected override void OnAppearanceChange(EntityUid uid, WFShowerComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite)
            return;

        var on = AppearanceSystem.TryGetData<bool>(uid, WFShowerVisuals.On, out var isOn, args.Component) && isOn;
        var mist = AppearanceSystem.TryGetData<bool>(uid, WFShowerVisuals.Mist, out var hasMist, args.Component) && hasMist;
        var ent = (uid, sprite);

        SpriteSystem.LayerSetVisible(ent, WFShowerVisualLayers.Water, on);
        SpriteSystem.LayerSetVisible(ent, WFShowerVisualLayers.Mist, mist);

        if (!AppearanceSystem.TryGetData<Color>(uid, WFShowerVisuals.Color, out var color, args.Component))
            return;

        // The sprite is already water-blue, so pull the tint halfway to white to keep plain water looking clear.
        var tint = Color.InterpolateBetween(Color.White, color.WithAlpha(1f), 0.6f);
        SpriteSystem.LayerSetColor(ent, WFShowerVisualLayers.Water, tint);
        SpriteSystem.LayerSetColor(ent, WFShowerVisualLayers.Mist, tint.WithAlpha(0.8f));
    }
}
