using Content.Shared._WG.Remade.Corruption;
using Robust.Client.GameObjects;

namespace Content.Client._WG.Remade.Corruption;

/// <summary>Sets a corruption or wall-growth sprite to the state its neighbours call for.</summary>
public sealed class WGCorruptionVisualizerSystem : VisualizerSystem<WGCorruptionVisualsComponent>
{
    private const int Full = 255;

    protected override void OnAppearanceChange(EntityUid uid, WGCorruptionVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !AppearanceSystem.TryGetData<int>(uid, WGCorruptionVisuals.Mask, out var mask, args.Component))
            return;

        var state = component.Prefix + mask;
        if (mask == Full && component.FullStates.Count > 0)
            state = component.FullStates[(int) ((uint) uid.Id % (uint) component.FullStates.Count)];

        SpriteSystem.LayerSetRsiState((uid, args.Sprite), component.Layer, state);
    }
}
