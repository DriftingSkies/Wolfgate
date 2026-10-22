using Content.Shared._WF.Barricades;
using Content.Shared.Toggleable;
using Robust.Client.GameObjects;
using static Content.Shared._WF.Barricades.EntrenchingToolComponentVisualLayers;

namespace Content.Client._WF.Barricades;

/// <summary>Shows the entrenching tool folded or open, with dirt on the blade while it holds some.</summary>
public sealed class EntrenchingToolVisualsSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private static readonly Color DirtColor = Color.FromHex("#C04000");

    public override void Initialize()
    {
        SubscribeLocalEvent<EntrenchingToolComponent, AfterAutoHandleStateEvent>(OnHandleState);
        SubscribeLocalEvent<EntrenchingToolComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnHandleState(Entity<EntrenchingToolComponent> tool, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisuals(tool);
    }

    private void OnAppearanceChange(Entity<EntrenchingToolComponent> tool, ref AppearanceChangeEvent args)
    {
        UpdateVisuals(tool);
    }

    private void UpdateVisuals(Entity<EntrenchingToolComponent> tool)
    {
        if (!TryComp(tool, out SpriteComponent? sprite))
            return;

        var open = _appearance.TryGetData(tool, ToggleableVisuals.Enabled, out bool toggled) && toggled;
        Entity<SpriteComponent?> ent = (tool.Owner, sprite);

        if (_sprite.LayerMapTryGet(ent, Base, out var baseLayer, false))
            _sprite.LayerSetVisible(ent, baseLayer, open);

        if (_sprite.LayerMapTryGet(ent, Folded, out var foldedLayer, false))
            _sprite.LayerSetVisible(ent, foldedLayer, !open);

        if (_sprite.LayerMapTryGet(ent, Dirt, out var dirtLayer, false))
        {
            var dirty = open && tool.Comp.TotalLayers > 0;
            _sprite.LayerSetVisible(ent, dirtLayer, dirty);
            if (dirty)
                _sprite.LayerSetColor(ent, dirtLayer, DirtColor);
        }
    }
}
