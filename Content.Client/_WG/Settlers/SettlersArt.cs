using Content.Shared._WG.Settlers;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Utility;

namespace Content.Client._WG.Settlers;

/// <summary>The colours, icons and fonts the Settlers board and window share.</summary>
public static class SettlersArt
{
    public static readonly Color[] SeatColors =
    {
        Color.FromHex("#2EC4B6"),
        Color.FromHex("#F2C14E"),
        Color.FromHex("#ECECEC"),
        Color.FromHex("#E4572E"),
    };

    /// <summary>Sector fill colours, by <see cref="SettlersSector"/>.</summary>
    public static readonly Color[] SectorColors =
    {
        Color.FromHex("#8C6A4F"),
        Color.FromHex("#9CCFE0"),
        Color.FromHex("#C77DBA"),
        Color.FromHex("#6AA84F"),
        Color.FromHex("#7C8390"),
        Color.FromHex("#3A3152"),
    };

    public static readonly Color AnyDockColor = Color.FromHex("#3A4260");
    public static readonly Color Space = Color.FromHex("#090C17");
    public static readonly Color Rim = Color.FromHex("#161D33");
    public static readonly Color Token = Color.FromHex("#ECE4CC");
    public static readonly Color TokenText = Color.FromHex("#1B1B1F");
    public static readonly Color TokenHot = Color.FromHex("#B3261E");
    public static readonly Color Rolled = Color.FromHex("#FFD54A");
    public static readonly Color Outline = Color.FromHex("#0B0B0E");
    public static readonly Color Coast = Color.FromHex("#1B2544");

    /// <summary>The side panel: section boxes, their headings and quiet text.</summary>
    public static readonly Color PanelColor = Color.FromHex("#14161D");
    public static readonly Color PanelEdge = Color.FromHex("#2A2E3B");
    public static readonly Color Heading = Color.FromHex("#D9B45A");
    public static readonly Color Muted = Color.FromHex("#8C93A8");
    public static readonly Color Dim = new(1f, 1f, 1f, 0.4f);

    private static readonly ResPath ResourcesRsi = new("/Textures/_WG/Settlers/resources.rsi");

    /// <summary>Resource icons, each in its sector's colour.</summary>
    private static readonly SpriteSpecifier.Rsi[] ResourceIcons =
    {
        new(ResourcesRsi, "scrap"),
        new(ResourcesRsi, "ice"),
        new(ResourcesRsi, "gas"),
        new(ResourcesRsi, "biomass"),
        new(ResourcesRsi, "ore"),
    };

    private static readonly SpriteSpecifier.Rsi PirateIcon = new(new ResPath("/Textures/Clothing/Head/Bandanas/skull.rsi"), "icon");

    public static Texture ResourceTexture(int resource)
    {
        return IoCManager.Resolve<IEntityManager>().System<SpriteSystem>().Frame0(ResourceIcons[resource]);
    }

    public static Texture PirateTexture()
    {
        return IoCManager.Resolve<IEntityManager>().System<SpriteSystem>().Frame0(PirateIcon);
    }

    /// <summary>The colour of the sector that yields this resource.</summary>
    public static Color ResourceColor(int resource)
    {
        return SectorColors[resource];
    }

    public static StyleBoxFlat Box(Color background, Color border, float thickness = 1f, float marginX = 8f, float marginY = 6f)
    {
        var box = new StyleBoxFlat { BackgroundColor = background, BorderColor = border, BorderThickness = new Thickness(thickness) };
        box.SetContentMarginOverride(StyleBox.Margin.Horizontal, marginX);
        box.SetContentMarginOverride(StyleBox.Margin.Vertical, marginY);
        return box;
    }

    public static Font Font(int size, bool bold = true)
    {
        var cache = IoCManager.Resolve<IResourceCache>();
        var path = bold ? "/Fonts/NotoSans/NotoSans-Bold.ttf" : "/Fonts/NotoSans/NotoSans-Regular.ttf";
        return new VectorFont(cache.GetResource<FontResource>(path), size);
    }

    public static string ResourceName(int resource)
    {
        return Loc.GetString($"wg-settlers-resource-{((SettlersResource) resource).ToString().ToLowerInvariant()}");
    }

    public static string CardName(SettlersCard card)
    {
        return Loc.GetString($"wg-settlers-card-{card.ToString().ToLowerInvariant()}");
    }

    /// <summary>A list such as "1 Scrap, 1 Ice", or the word for nothing.</summary>
    public static string Counts(int[] counts)
    {
        var parts = new List<string>();
        for (var resource = 0; resource < counts.Length; resource++)
        {
            if (counts[resource] > 0)
                parts.Add(Loc.GetString("wg-settlers-count", ("count", counts[resource]), ("resource", ResourceName(resource))));
        }

        return parts.Count == 0 ? Loc.GetString("wg-settlers-nothing") : string.Join(", ", parts);
    }
}
