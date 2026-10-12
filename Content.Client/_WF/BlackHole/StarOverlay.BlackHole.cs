using System.Numerics;
using Content.Client._WF.BlackHole;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Robust.Client.Graphics;

namespace Content.Client._FarHorizons.StarSystem;

/// <summary>Refracts the parallax behind an accreting black hole before ships are drawn.</summary>
public sealed partial class StarOverlay
{
    private const string BlackHoleShader = "WFBlackHole";

    /// <summary>Only black holes need a copy of the background for refraction.</summary>
    public override bool RequestScreenTexture => _star?.Shader == BlackHoleShader;

    /// <summary>Culls only black holes, using the same footprint and parallax as the shader.</summary>
    private static bool BlackHoleVisible(Star star, in OverlayDrawArgs args)
    {
        return star.Shader != BlackHoleShader || BlackHoleVisuals.IsVisible(star.Position, star.Radius,
            args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center, args.WorldAABB);
    }

    /// <summary>Projects world displacements into fragment pixels, including camera rotation and zoom.</summary>
    private void PrepareBlackHoleShader(in OverlayDrawArgs args)
    {
        if (_star?.Shader != BlackHoleShader || _shaderInstance == null)
            return;

        _shaderInstance.SetParameter("parallaxFactor", BlackHoleVisuals.ParallaxFactor);
        _shaderInstance.SetParameter("solarRadiusFactor", BlackHoleVisuals.SolarRadiusFactor);
        _shaderInstance.SetParameter("effectRadius", BlackHoleVisuals.EffectRadius);
        _shaderInstance.SetParameter("hasBackground", ScreenTexture != null);
        if (ScreenTexture == null)
            return;

        var origin = args.Viewport.WorldToLocal(Vector2.Zero);
        var right = args.Viewport.WorldToLocal(Vector2.UnitX) - origin;
        var up = args.Viewport.WorldToLocal(Vector2.UnitY) - origin;
        right.Y = -right.Y;
        up.Y = -up.Y;
        _shaderInstance.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shaderInstance.SetParameter("worldToScreenX", right);
        _shaderInstance.SetParameter("worldToScreenY", up);
    }
}
