using System.Numerics;
using Content.Client.Resources;
using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.Client._WG.PdaUi;

/// <summary>
/// Colours, fonts and boxes for the PDA surface. Values follow Wolfgate's Retro skin (amber readouts,
/// warm charcoal, dusty olive) so the device reads as part of the game's interface, not a stock one.
/// </summary>
public static class PdaPalette
{
    /// <summary>Wolfgate Retro skin texture folder, shared with the game stylesheet.</summary>
    public const string SkinPath = "/Textures/_WG/Stylesheets/Interface/WolfgateRetro/Style/";

    // Window and surfaces.
    public static readonly Color Base = Color.FromHex("#141412");
    public static readonly Color Surface = Color.FromHex("#1C1B18");
    public static readonly Color SurfaceRaised = Color.FromHex("#23221E");
    public static readonly Color Hover = Color.FromHex("#3D3B34");
    public static readonly Color Pressed = Color.FromHex("#6A4A1E");
    public static readonly Color Border = Color.FromHex("#39362D");
    public static readonly Color BorderLight = Color.FromHex("#4B4739");
    public static readonly Color TitleBar = Color.FromHex("#23221E");
    public static readonly Color HeaderBar = Color.FromHex("#141412");
    public static readonly Color SectionLine = Color.FromHex("#A8702E");

    // Text and accents.
    public static readonly Color Primary = Color.FromHex("#2A2925");
    public static readonly Color Accent = Color.FromHex("#F2A54A");
    public static readonly Color AccentDim = Color.FromHex("#A8702E");
    public static readonly Color Text = Color.FromHex("#EDE6D2");
    public static readonly Color TextSoft = Color.FromHex("#D9D2BF");
    public static readonly Color TextMuted = Color.FromHex("#A39E8C");
    public static readonly Color TextDim = Color.FromHex("#7E7A6C");
    public static readonly Color IconTint = Color.FromHex("#C9C2AD");

    // Status.
    public static readonly Color Good = Color.FromHex("#A6C85A");
    public static readonly Color Caution = Color.FromHex("#F2C14E");
    public static readonly Color Bad = Color.FromHex("#E0645A");

    /// <summary>Monospace face for readouts, rows and menu entries.</summary>
    public static Font Mono(IResourceCache cache, int size)
    {
        return cache.GetFont("/Fonts/RobotoMono/RobotoMono-Bold.ttf", size);
    }

    /// <summary>Sans face for application content.</summary>
    public static Font Sans(IResourceCache cache, int size)
    {
        return cache.NotoStack(size: size);
    }

    /// <summary>Italic sans face, used for secondary labels.</summary>
    public static Font SansItalic(IResourceCache cache, int size)
    {
        return cache.NotoStack(variation: "Italic", size: size);
    }

    /// <summary>Display face of the Retro skin, used for the window and section titles.</summary>
    public static Font Display(IResourceCache cache, int size)
    {
        return cache.GetFont("/Fonts/Boxfont-round/Boxfont Round.ttf", size);
    }

    /// <summary>Retro skin texture as a nine-patch panel.</summary>
    public static StyleBoxTexture PanelTexture(IResourceCache cache, string file, float patch, Color? modulate = null)
    {
        var box = new StyleBoxTexture
        {
            Texture = cache.GetTexture(SkinPath + file),
            Modulate = modulate ?? Color.White,
        };
        box.SetPatchMargin(StyleBox.Margin.All, patch);
        return box;
    }

    /// <summary>Retro skin window header: shaped left cap, thin right cap, inset content.</summary>
    public static StyleBoxTexture HeaderTexture(IResourceCache cache, string file = "window_header.png")
    {
        return new StyleBoxTexture
        {
            Texture = cache.GetTexture(SkinPath + file),
            PatchMarginLeft = 10,
            PatchMarginRight = 2,
            PatchMarginTop = 6,
            PatchMarginBottom = 4,
            ContentMarginLeftOverride = 8,
            ContentMarginTopOverride = 2,
            ContentMarginRightOverride = 4,
            ContentMarginBottomOverride = 3,
        };
    }

    /// <summary>Tiled overlay used for the faint screen scanlines.</summary>
    public static StyleBoxTexture TiledTexture(IResourceCache cache, string file)
    {
        return new StyleBoxTexture
        {
            Texture = cache.GetTexture(SkinPath + file),
            Mode = StyleBoxTexture.StretchMode.Tile,
        };
    }

    /// <summary>Flat box with optional border, content margins overridden so borders do not pad.</summary>
    public static StyleBoxFlat Box(Color background, Color? border = null, Thickness? thickness = null, Thickness? content = null)
    {
        var box = new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border ?? Color.Transparent,
            BorderThickness = thickness ?? new Thickness(0),
        };

        var inner = content ?? new Thickness(0);
        box.SetContentMarginOverride(StyleBox.Margin.Left, inner.Left);
        box.SetContentMarginOverride(StyleBox.Margin.Top, inner.Top);
        box.SetContentMarginOverride(StyleBox.Margin.Right, inner.Right);
        box.SetContentMarginOverride(StyleBox.Margin.Bottom, inner.Bottom);
        return box;
    }
}
