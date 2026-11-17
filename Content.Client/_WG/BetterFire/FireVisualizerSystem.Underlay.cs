using Content.Client.Atmos.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client.Atmos.EntitySystems;

/// <summary>
/// Draws fire behind burning entities: while the fire layer shows state <c>X</c>, a state <c>X_underlay</c> in the
/// same RSI is shown at the bottom of the sprite.
/// </summary>
public sealed partial class FireVisualizerSystem
{
    private const string UnderlaySuffix = "_underlay";

    /// <summary>
    /// Adds the underlay layer below every other layer if the fire RSI has an underlay for either fire state.
    /// </summary>
    private void AddFireUnderlay(EntityUid uid, FireVisualsComponent component, SpriteComponent sprite)
    {
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), FireVisualLayers.Fire, out var fire, false)
            || SpriteSystem.LayerGetEffectiveRsi((uid, sprite), fire) is not { } rsi
            || !HasUnderlay(rsi, component.NormalState) && !HasUnderlay(rsi, component.AlternateState))
            return;

        SpriteSystem.AddBlankLayer((uid, sprite), 0);
        SpriteSystem.LayerMapSet((uid, sprite), FireUnderlayVisualLayers.Underlay, 0);
        SpriteSystem.LayerSetRsi((uid, sprite), FireUnderlayVisualLayers.Underlay, rsi);
        SpriteSystem.LayerSetVisible((uid, sprite), FireUnderlayVisualLayers.Underlay, false);
        sprite.LayerSetShader(FireUnderlayVisualLayers.Underlay, "unshaded");
    }

    /// <summary>
    /// Shows the underlay of the fire state picked for these stacks, or hides it.
    /// </summary>
    private void UpdateFireUnderlay(EntityUid uid, FireVisualsComponent component, SpriteComponent sprite, bool onFire, float fireStacks)
    {
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), FireUnderlayVisualLayers.Underlay, out var index, false))
            return;

        // Same pick as UpdateAppearance.
        var state = fireStacks > component.FireStackAlternateState && !string.IsNullOrEmpty(component.AlternateState)
            ? component.AlternateState
            : component.NormalState;

        var visible = onFire
            && SpriteSystem.LayerGetEffectiveRsi((uid, sprite), index) is { } rsi
            && HasUnderlay(rsi, state);

        SpriteSystem.LayerSetVisible((uid, sprite), index, visible);
        if (visible)
            SpriteSystem.LayerSetRsiState((uid, sprite), index, state + UnderlaySuffix);
    }

    private void RemoveFireUnderlay(EntityUid uid)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            SpriteSystem.RemoveLayer((uid, sprite), FireUnderlayVisualLayers.Underlay, false);
    }

    private static bool HasUnderlay(RSI rsi, string? state)
    {
        return !string.IsNullOrEmpty(state) && rsi.TryGetState(state + UnderlaySuffix, out _);
    }
}

/// <summary>Layer key for the fire underlay drawn below a burning sprite.</summary>
public enum FireUnderlayVisualLayers : byte
{
    Underlay,
}
