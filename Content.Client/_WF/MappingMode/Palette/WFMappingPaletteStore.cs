using System.IO;
using YamlDotNet.RepresentationModel;

namespace Content.Client._WF.MappingMode.Palette;

/// <summary>
/// The mapping palette's own memory: tile and decal favourites and recents, and the strip of recent entries of any
/// kind. Entity favourites stay in the spawn menu's cvar. Read from and written to YAML text; no file access.
/// </summary>
public sealed class WFMappingPaletteStore
{
    /// <summary>How many tiles or decals each Recent list keeps.</summary>
    public const int RecentLimit = 40;

    /// <summary>How many entries the strip under the palette keeps.</summary>
    public const int StripLimit = 12;

    private readonly HashSet<string> _tileFavourites = new();
    private readonly HashSet<string> _decalFavourites = new();
    private readonly List<string> _tileRecent = new();
    private readonly List<string> _decalRecent = new();
    private readonly List<WFMappingPaletteEntry> _strip = new();

    /// <summary>The newest entries of any kind, newest first.</summary>
    public IReadOnlyList<WFMappingPaletteEntry> Strip => _strip;

    public IReadOnlyCollection<string> Favourites(WFMappingPaletteKind kind)
    {
        return kind == WFMappingPaletteKind.Decal ? _decalFavourites : _tileFavourites;
    }

    /// <summary>Recent tile or decal ids, newest first.</summary>
    public IReadOnlyList<string> Recent(WFMappingPaletteKind kind)
    {
        return kind == WFMappingPaletteKind.Decal ? _decalRecent : _tileRecent;
    }

    public bool IsFavourite(WFMappingPaletteKind kind, string id)
    {
        return kind != WFMappingPaletteKind.Entity && FavouriteSet(kind).Contains(id);
    }

    /// <summary>Adds or removes a tile or decal favourite.</summary>
    public void ToggleFavourite(WFMappingPaletteKind kind, string id)
    {
        if (kind == WFMappingPaletteKind.Entity)
            return;

        var set = FavouriteSet(kind);
        if (!set.Remove(id))
            set.Add(id);
    }

    /// <summary>Puts an entry at the front of the strip, and a tile or decal at the front of its Recent list.</summary>
    public void AddRecent(WFMappingPaletteEntry entry)
    {
        _strip.RemoveAll(e => e.Kind == entry.Kind && e.Id == entry.Id);
        _strip.Insert(0, entry);
        if (_strip.Count > StripLimit)
            _strip.RemoveRange(StripLimit, _strip.Count - StripLimit);

        if (entry.Kind == WFMappingPaletteKind.Entity)
            return;

        var recent = RecentList(entry.Kind);
        recent.Remove(entry.Id);
        recent.Insert(0, entry.Id);
        if (recent.Count > RecentLimit)
            recent.RemoveRange(RecentLimit, recent.Count - RecentLimit);
    }

    /// <summary>Reads a store; anything malformed is skipped, and unreadable text gives an empty store.</summary>
    public static WFMappingPaletteStore Read(string text)
    {
        var store = new WFMappingPaletteStore();
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception)
        {
            return store;
        }

        if (root == null)
            return store;

        ReadKind(root, "tiles", store._tileFavourites, store._tileRecent);
        ReadKind(root, "decals", store._decalFavourites, store._decalRecent);

        if (Child(root, "strip") is YamlSequenceNode strip)
        {
            foreach (var node in strip.Children)
            {
                if (node is not YamlMappingNode item ||
                    Scalar(item, "id") is not { Length: > 0 } id ||
                    ParseKind(Scalar(item, "kind")) is not { } kind)
                {
                    continue;
                }

                Color? color = Scalar(item, "color") is { } hex && Color.TryFromHex(hex, out var parsed) ? parsed : null;
                if (store._strip.Count < StripLimit)
                    store._strip.Add(new WFMappingPaletteEntry(kind, id, color));
            }
        }

        return store;
    }

    /// <summary>The store as YAML text.</summary>
    public string Write()
    {
        var root = new YamlMappingNode();
        root.Add("tiles", WriteKind(_tileFavourites, _tileRecent));
        root.Add("decals", WriteKind(_decalFavourites, _decalRecent));

        var strip = new YamlSequenceNode();
        foreach (var entry in _strip)
        {
            var item = new YamlMappingNode();
            item.Add("kind", KindName(entry.Kind));
            item.Add("id", entry.Id);
            if (entry.Color is { } color)
                item.Add("color", color.ToHex());

            strip.Add(item);
        }

        root.Add("strip", strip);

        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, false);
        return writer.ToString();
    }

    private HashSet<string> FavouriteSet(WFMappingPaletteKind kind)
    {
        return kind == WFMappingPaletteKind.Decal ? _decalFavourites : _tileFavourites;
    }

    private List<string> RecentList(WFMappingPaletteKind kind)
    {
        return kind == WFMappingPaletteKind.Decal ? _decalRecent : _tileRecent;
    }

    private static void ReadKind(YamlMappingNode root, string key, HashSet<string> favourites, List<string> recent)
    {
        if (Child(root, key) is not YamlMappingNode node)
            return;

        if (Child(node, "favourites") is YamlSequenceNode favouriteNodes)
        {
            foreach (var item in favouriteNodes.Children)
            {
                if (item is YamlScalarNode { Value: { Length: > 0 } id })
                    favourites.Add(id);
            }
        }

        if (Child(node, "recent") is YamlSequenceNode recentNodes)
        {
            foreach (var item in recentNodes.Children)
            {
                if (item is YamlScalarNode { Value: { Length: > 0 } id } && !recent.Contains(id) &&
                    recent.Count < RecentLimit)
                {
                    recent.Add(id);
                }
            }
        }
    }

    private static YamlMappingNode WriteKind(HashSet<string> favourites, List<string> recent)
    {
        var favouriteNodes = new YamlSequenceNode();
        var sorted = new List<string>(favourites);
        sorted.Sort(StringComparer.Ordinal);
        foreach (var id in sorted)
        {
            favouriteNodes.Add(new YamlScalarNode(id));
        }

        var recentNodes = new YamlSequenceNode();
        foreach (var id in recent)
        {
            recentNodes.Add(new YamlScalarNode(id));
        }

        var node = new YamlMappingNode();
        node.Add("favourites", favouriteNodes);
        node.Add("recent", recentNodes);
        return node;
    }

    /// <summary>A mapping's value by key; enumerated, since the sandbox doesn't allow its dictionary.</summary>
    private static YamlNode? Child(YamlMappingNode node, string key)
    {
        foreach (var pair in node)
        {
            if (pair.Key is YamlScalarNode { Value: { } name } && name == key)
                return pair.Value;
        }

        return null;
    }

    private static string? Scalar(YamlMappingNode node, string key)
    {
        return Child(node, key) is YamlScalarNode scalar ? scalar.Value : null;
    }

    private static string KindName(WFMappingPaletteKind kind)
    {
        return kind switch
        {
            WFMappingPaletteKind.Tile => "tile",
            WFMappingPaletteKind.Decal => "decal",
            _ => "entity",
        };
    }

    private static WFMappingPaletteKind? ParseKind(string? name)
    {
        return name switch
        {
            "entity" => WFMappingPaletteKind.Entity,
            "tile" => WFMappingPaletteKind.Tile,
            "decal" => WFMappingPaletteKind.Decal,
            _ => null,
        };
    }
}
