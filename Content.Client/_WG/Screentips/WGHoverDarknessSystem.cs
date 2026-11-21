using Content.Client.Viewport;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Light;

namespace Content.Client._WG.Screentips;

/// <summary>
/// Decides whether a hovered player or mob is hidden by darkness, so the screentip and hover outline skip it.
/// </summary>
public sealed class WGHoverDarknessSystem : EntitySystem
{
    [Dependency] private ILightManager _lightManager = default!;
    [Dependency] private LightLevelSystem _light = default!;

    /// <summary>Below this light level a mob is in the dark.</summary>
    private const float DarkLevel = 0.1f;

    /// <summary>Whether <paramref name="entity"/> should not be revealed by hovering in <paramref name="viewport"/>.</summary>
    public bool HiddenInDark(EntityUid entity, IViewportControl viewport)
    {
        return DrawsLight(viewport) && InDark(entity);
    }

    /// <summary>Whether <paramref name="entity"/> is a player or mob standing where the light is too dim to see it.</summary>
    public bool InDark(EntityUid entity)
    {
        return HasComp<MobStateComponent>(entity)
               && _light.TryCalculateLightLevel(entity, out var level)
               && level < DarkLevel;
    }

    /// <summary>Whether the view draws lighting at all; with it off (a ghost's toggle, mapping) everything is visible.</summary>
    private bool DrawsLight(IViewportControl viewport)
    {
        return _lightManager.Enabled && (viewport is not ScalingViewport { Eye: { } eye } || eye.DrawLight);
    }
}
