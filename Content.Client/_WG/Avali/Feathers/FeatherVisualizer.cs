using Content.Shared._WG.Avali.Feathers;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Item;
using Robust.Client.GameObjects;

namespace Content.Client._WG.Avali.Feathers;

/// <summary>Applies owner and blood colors to feather sprites.</summary>
public sealed partial class FeatherVisualizer : VisualizerSystem<FeatherComponent>
{
    [Dependency] private ClothingSystem _clothing = default!;
    [Dependency] private SharedItemSystem _item = default!;

    protected override void OnAppearanceChange(EntityUid uid, FeatherComponent component, ref AppearanceChangeEvent args)
    {
        var featherColor = AppearanceSystem.TryGetData<Color>(uid, FeatherVisuals.FeatherColor, out var color,
            args.Component) ? color : Color.White;
        var bloodColor = AppearanceSystem.TryGetData<Color>(uid, FeatherVisuals.BloodColor, out color,
            args.Component) ? color : Color.Transparent;

        SpriteSystem.LayerSetColor(uid, FeatherVisualLayers.Feather, featherColor);
        SpriteSystem.LayerSetColor(uid, FeatherVisualLayers.Blood, bloodColor);

        if (!TryComp<ClothingComponent>(uid, out var clothing))
            return;

        foreach (var slot in clothing.ClothingVisuals.Keys)
            _clothing.SetLayerColor(clothing, slot, $"{slot}-feather", featherColor);

        // Rebuild worn visuals from the new color to avoid stale layers when slots are swapped quickly.
        _item.VisualsChanged(uid);
    }
}
