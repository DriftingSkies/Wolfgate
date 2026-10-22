using Content.Shared.Decals;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.MappingMode.Palette;

/// <summary>Icons and names of palette entries, for the palette tabs, the recent strip and the properties panel.</summary>
public sealed partial class WFMappingPaletteIcons
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private readonly Dictionary<string, Texture?> _tileIcons = new();

    public WFMappingPaletteIcons()
    {
        IoCManager.InjectDependencies(this);
    }

    public Texture? Get(WFMappingPaletteEntry entry)
    {
        return entry.Kind switch
        {
            WFMappingPaletteKind.Tile => Tile(entry.Id),
            WFMappingPaletteKind.Decal => Decal(entry.Id),
            _ => Entity(entry.Id),
        };
    }

    public Texture? Entity(string id)
    {
        if (!_prototypes.HasIndex<EntityPrototype>(id) || !_entities.TrySystem<SpriteSystem>(out var sprite))
            return null;

        return sprite.GetPrototypeIcon(id).Default;
    }

    /// <summary>The first variant of a tile's sprite.</summary>
    public Texture? Tile(string id)
    {
        if (_tileIcons.TryGetValue(id, out var cached))
            return cached;

        Texture? icon = null;
        if (_tileDefs.TryGetDefinition(id, out var tile) && tile.Sprite is { } path)
        {
            if (path.Directory.Extension == "rsi")
            {
                // An RSI frame is already one tile cell.
                icon = RsiFrame(path);
            }
            else if (_resources.TryGetResource<TextureResource>(path, out var resource))
            {
                var texture = resource.Texture;
                var variants = Math.Max(1, (int) tile.Variants);
                icon = SubRegion(texture, UIBox2.FromDimensions(0, 0, texture.Width / variants, texture.Height));
            }
        }

        _tileIcons[id] = icon;
        return icon;
    }

    /// <summary>A tile sprite inside an RSI, read through the RSI's state, as raw loads there warn.</summary>
    private Texture? RsiFrame(ResPath path)
    {
        return _resources.TryGetResource<RSIResource>(path.Directory, out var rsi) &&
               rsi.RSI.TryGetState(path.FilenameWithoutExtension, out var state)
            ? state.Frame0
            : null;
    }

    /// <summary>
    /// A region of a texture. An atlas is cut from its own source, never wrapped: Clyde draws an atlas through the
    /// source's GPU texture and dereferences null for an atlas of an atlas, which aborts the whole UI root's draw.
    /// </summary>
    public static AtlasTexture SubRegion(Texture texture, UIBox2 region)
    {
        if (texture is AtlasTexture atlas)
            return new AtlasTexture(atlas.SourceTexture, region.Translated(atlas.SubRegion.TopLeft));

        return new AtlasTexture(texture, region);
    }

    public Texture? Decal(string id)
    {
        if (!_prototypes.TryIndex<DecalPrototype>(id, out var decal) || !_entities.TrySystem<SpriteSystem>(out var sprite))
            return null;

        return sprite.Frame0(decal.Sprite);
    }

    /// <summary>The name a player reads: the entity's name, the tile's name, or the decal's id.</summary>
    public string Name(WFMappingPaletteEntry entry)
    {
        switch (entry.Kind)
        {
            case WFMappingPaletteKind.Tile:
                return _tileDefs.TryGetDefinition(entry.Id, out var tile) ? Loc.GetString(tile.Name) : entry.Id;
            case WFMappingPaletteKind.Decal:
                return entry.Id;
            default:
                return _prototypes.TryIndex<EntityPrototype>(entry.Id, out var prototype) ? prototype.Name : entry.Id;
        }
    }
}

/// <summary>The right-click menu of a palette entry.</summary>
public static class WFMappingEntryMenu
{
    /// <summary>Opens a small menu at the mouse with one button per item.</summary>
    public static void Open(params (string Text, Action Action)[] items)
    {
        var popup = new Popup();
        var panel = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#1b1b1e"),
                BorderColor = Color.FromHex("#4a4a55"),
                BorderThickness = new Thickness(1),
            },
        };
        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MinWidth = 180,
            Margin = new Thickness(2),
        };

        foreach (var (text, action) in items)
        {
            var button = new Button { Text = text, HorizontalExpand = true };
            button.OnPressed += _ =>
            {
                popup.Close();
                action();
            };
            box.AddChild(button);
        }

        panel.AddChild(box);
        popup.AddChild(panel);
        popup.OpenAtMouse();
    }
}
