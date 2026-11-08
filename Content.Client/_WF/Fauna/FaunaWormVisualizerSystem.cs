using Content.Shared._WF.Fauna;
using Robust.Client.GameObjects;

namespace Content.Client._WF.Fauna;

/// <summary>
/// Sets a worm part's base layer to the state the server picked. Body segments turn with their entity, so they can
/// bend along the path.
/// </summary>
public sealed partial class FaunaWormVisualizerSystem : VisualizerSystem<FaunaWormVisualsComponent>
{
    private const string HeadPrefix = "spacewormhead";

    protected override void OnAppearanceChange(EntityUid uid,
        FaunaWormVisualsComponent component,
        ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null
            || !AppearanceSystem.TryGetData<string>(uid, FaunaWormVisuals.State, out var state, args.Component)
            || !SpriteSystem.LayerMapTryGet((uid, args.Sprite), FaunaWormVisualLayers.Base, out var layer, false))
            return;

        SpriteSystem.LayerSetRsiState((uid, args.Sprite), layer, state);
        args.Sprite.NoRotation = state.StartsWith(HeadPrefix);
    }
}
