using Content.Client.Atmos.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;

namespace Content.Client.Atmos.EntitySystems;

/// <summary>
/// Lets directional flames burn on sprites that snap to cardinals, such as the machines that catch fire. A snapped
/// layer may only have one direction, and the mob fire states have four.
/// </summary>
public sealed partial class FireVisualizerSystem
{
    /// <summary>
    /// Gives the fire layers of a snapped sprite a rendering strategy of their own, so they turn as they do on a mob
    /// while every other layer keeps snapping.
    /// </summary>
    private void UnsnapFireLayers(EntityUid uid, FireVisualsComponent component, SpriteComponent sprite)
    {
        if (!sprite.SnapCardinals
            || !SpriteSystem.LayerMapTryGet((uid, sprite), FireVisualLayers.Fire, out var fire, false)
            || SpriteSystem.LayerGetEffectiveRsi((uid, sprite), fire) is not { } rsi
            || !IsDirectional(rsi, component.NormalState) && !IsDirectional(rsi, component.AlternateState))
            return;

        // Layers that name no strategy keep the sprite's.
        SpriteSystem.SetGranularLayersRendering((uid, sprite), true);
        var strategy = sprite.NoRotation ? LayerRenderingStrategy.NoRotation : LayerRenderingStrategy.Default;
        SpriteSystem.LayerSetRenderingStrategy((uid, sprite), fire, strategy);
        if (SpriteSystem.LayerMapTryGet((uid, sprite), FireUnderlayVisualLayers.Underlay, out var underlay, false))
            SpriteSystem.LayerSetRenderingStrategy((uid, sprite), underlay, strategy);
    }

    private static bool IsDirectional(RSI rsi, string? state)
    {
        return !string.IsNullOrEmpty(state)
            && rsi.TryGetState(state, out var found)
            && found.RsiDirections != RsiDirectionType.Dir1;
    }
}
