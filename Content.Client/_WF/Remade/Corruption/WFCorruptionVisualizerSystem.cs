using Content.Shared._WF.Remade.Corruption;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Remade.Corruption;

/// <summary>Sets a corruption or wall-growth sprite to the state its neighbours call for.</summary>
public sealed class WFCorruptionVisualizerSystem : VisualizerSystem<WFCorruptionVisualsComponent>
{
    private const int Full = 255;

    protected override void OnAppearanceChange(EntityUid uid, WFCorruptionVisualsComponent component, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !AppearanceSystem.TryGetData<int>(uid, WFCorruptionVisuals.Mask, out var mask, args.Component))
            return;

        var state = component.Prefix + mask;
        if (mask == Full && component.FullStates.Count > 0)
            state = component.FullStates[(int) ((uint) uid.Id % (uint) component.FullStates.Count)];

        SpriteSystem.LayerSetRsiState((uid, args.Sprite), component.Layer, state);
    }
}
