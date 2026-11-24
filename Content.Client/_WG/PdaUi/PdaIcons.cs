using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WG.PdaUi;

/// <summary>
/// Window chrome glyphs, drawn from the same icon font as <see cref="PdaProgramIcons"/> so every
/// button has the same visual weight whatever its source art.
/// </summary>
public static class PdaIcons
{
    private static readonly ResPath IconPath = new("/Textures/_WG/PdaUi/ui_icons.rsi");

    public static readonly SpriteSpecifier Home = Rsi("home");
    public static readonly SpriteSpecifier Settings = Rsi("settings");
    public static readonly SpriteSpecifier Info = Rsi("info");
    public static readonly SpriteSpecifier Light = Rsi("light");
    public static readonly SpriteSpecifier Pen = Rsi("pen");
    public static readonly SpriteSpecifier Id = Rsi("id");
    public static readonly SpriteSpecifier Pai = Rsi("pai");
    public static readonly SpriteSpecifier Close = Rsi("close");
    public static readonly SpriteSpecifier Clock = Rsi("clock");
    public static readonly SpriteSpecifier Power = Rsi("power");
    public static readonly SpriteSpecifier Program = Rsi("program");
    public static readonly SpriteSpecifier Disk = Rsi("disk");
    public static readonly SpriteSpecifier Eject = Rsi("eject");

    public static readonly SpriteSpecifier Logo = Rsi("paw");

    /// <summary>Sets the sprite and scales it to fit the given square, whatever the source resolution.</summary>
    public static void Apply(AnimatedTextureRect target, SpriteSpecifier specifier, float size)
    {
        target.SetFromSpriteSpecifier(specifier);
        target.DisplayRect.Stretch = TextureRect.StretchMode.KeepCentered;

        var texture = target.DisplayRect.Texture;
        if (texture == null)
            return;

        target.DisplayRect.TextureScale = new Vector2(size / texture.Width, size / texture.Height);
    }

    private static SpriteSpecifier.Rsi Rsi(string state)
    {
        return new SpriteSpecifier.Rsi(IconPath, state);
    }
}
