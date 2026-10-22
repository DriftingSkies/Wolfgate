using System.Globalization;
using System.IO;
using System.Text;
using Content.Client._WF.MappingMode.Palette;
using Robust.Shared.Utility;
using YamlDotNet.RepresentationModel;

namespace Content.Client._WF.MappingMode.QuickSelect;

/// <summary>One quick-select slot: what it places and an optional label the player gave it.</summary>
public readonly record struct WFMappingQuickSlotEntry(WFMappingPaletteEntry Entry, string? Label = null);

/// <summary>A named set of quick-select pages and the page last shown.</summary>
public sealed class WFMappingQuickSelectSet(string name)
{
    public string Name = name;
    public readonly List<WFMappingQuickSlotEntry?[]> Pages = new();
    public int ActivePage;
}

/// <summary>
/// The quick-select bar's model: named sets of pages of ten slots, the active set and page, and whether eyedropper
/// picks are added. Read from and written to YAML text; no file access.
/// </summary>
public sealed class WFMappingQuickSelectStore
{
    public const int MaxSets = 10;
    public const int MaxPages = 10;
    public const int SlotsPerPage = 10;
    public const int MaxNameLength = 32;
    public const int MaxIdLength = 128;

    private readonly List<WFMappingQuickSelectSet> _sets = new();
    private int _activeSet;

    private WFMappingQuickSelectStore()
    {
    }

    public IReadOnlyList<WFMappingQuickSelectSet> Sets => _sets;

    public int ActiveSet => _activeSet;

    public WFMappingQuickSelectSet Current => _sets[_activeSet];

    /// <summary>The slots of the current set's shown page.</summary>
    public WFMappingQuickSlotEntry?[] CurrentPage => Current.Pages[Current.ActivePage];

    /// <summary>Whether eyedropper picks go on the bar.</summary>
    public bool AddPicks { get; set; }

    /// <summary>One set of the given entries, ten to a page, in order; at least one page.</summary>
    public static WFMappingQuickSelectStore Create(string setName, IReadOnlyList<WFMappingPaletteEntry> entries)
    {
        var store = new WFMappingQuickSelectStore();
        var set = new WFMappingQuickSelectSet(NormalizeName(setName) ?? "1");
        for (var i = 0; i < entries.Count; i++)
        {
            if (i % SlotsPerPage == 0)
            {
                if (set.Pages.Count == MaxPages)
                    break;

                set.Pages.Add(new WFMappingQuickSlotEntry?[SlotsPerPage]);
            }

            set.Pages[^1][i % SlotsPerPage] = new WFMappingQuickSlotEntry(entries[i]);
        }

        if (set.Pages.Count == 0)
            set.Pages.Add(new WFMappingQuickSlotEntry?[SlotsPerPage]);

        store._sets.Add(set);
        return store;
    }

    /// <summary>
    /// The placement actions of <c>/mapping_actions.yml</c> in file order: every action event with an
    /// <c>entityType</c> or a <c>tileId</c>, without the eraser or repeats.
    /// </summary>
    public static List<WFMappingPaletteEntry> ParseActions(string text)
    {
        var entries = new List<WFMappingPaletteEntry>();
        YamlSequenceNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlSequenceNode : null;
        }
        catch (Exception)
        {
            return entries;
        }

        if (root == null)
            return entries;

        foreach (var node in root.Children)
        {
            if (node is not YamlMappingNode item ||
                Child(item, "action") is not YamlMappingNode action ||
                Child(action, "event") is not YamlMappingNode ev ||
                Scalar(ev, "eraser") is { } eraser && eraser.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            WFMappingPaletteEntry entry;
            if (Scalar(ev, "entityType") is { Length: > 0 } entity)
                entry = new WFMappingPaletteEntry(WFMappingPaletteKind.Entity, entity);
            else if (Scalar(ev, "tileId") is { Length: > 0 } tile)
                entry = new WFMappingPaletteEntry(WFMappingPaletteKind.Tile, tile);
            else
                continue;

            if (!entries.Contains(entry))
                entries.Add(entry);
        }

        return entries;
    }

    /// <summary>A name trimmed, without control characters, cut to <see cref="MaxNameLength"/>; null when empty.</summary>
    public static string? NormalizeName(string? raw)
    {
        if (raw == null)
            return null;

        var builder = new StringBuilder();
        foreach (var c in raw.Trim())
        {
            if (char.IsControl(c))
                continue;

            if (builder.Length == MaxNameLength)
                break;

            builder.Append(c);
        }

        var name = builder.ToString().Trim();
        return name.Length > 0 ? name : null;
    }

    public void SelectSet(int index)
    {
        if (index >= 0 && index < _sets.Count)
            _activeSet = index;
    }

    public void SelectPage(int index)
    {
        if (index >= 0 && index < Current.Pages.Count)
            Current.ActivePage = index;
    }

    /// <summary>Adds an empty set with one page and shows it; false for an empty name or full sets.</summary>
    public bool AddSet(string name)
    {
        if (_sets.Count >= MaxSets || NormalizeName(name) is not { } normalized)
            return false;

        var set = new WFMappingQuickSelectSet(normalized);
        set.Pages.Add(new WFMappingQuickSlotEntry?[SlotsPerPage]);
        _sets.Add(set);
        _activeSet = _sets.Count - 1;
        return true;
    }

    public bool RenameSet(int index, string name)
    {
        if (index < 0 || index >= _sets.Count || NormalizeName(name) is not { } normalized)
            return false;

        _sets[index].Name = normalized;
        return true;
    }

    /// <summary>Removes a set; the last one stays.</summary>
    public bool DeleteSet(int index)
    {
        if (_sets.Count <= 1 || index < 0 || index >= _sets.Count)
            return false;

        _sets.RemoveAt(index);
        if (_activeSet >= index && _activeSet > 0)
            _activeSet--;

        return true;
    }

    /// <summary>Adds an empty page to the current set and shows it; false when its pages are full.</summary>
    public bool AddPage()
    {
        var set = Current;
        if (set.Pages.Count >= MaxPages)
            return false;

        set.Pages.Add(new WFMappingQuickSlotEntry?[SlotsPerPage]);
        set.ActivePage = set.Pages.Count - 1;
        return true;
    }

    /// <summary>Removes a page of the current set; the last one stays.</summary>
    public bool DeletePage(int index)
    {
        var set = Current;
        if (set.Pages.Count <= 1 || index < 0 || index >= set.Pages.Count)
            return false;

        set.Pages.RemoveAt(index);
        if (set.ActivePage >= index && set.ActivePage > 0)
            set.ActivePage--;

        return true;
    }

    /// <summary>
    /// Puts an entry in the first empty slot of the shown page. Returns its slot, or the slot already holding it
    /// with <paramref name="added"/> false, or -1 when the page is full.
    /// </summary>
    public int Add(WFMappingPaletteEntry entry, out bool added)
    {
        added = false;
        if (entry.Id.Length == 0 || entry.Id.Length > MaxIdLength)
            return -1;

        var page = CurrentPage;
        for (var i = 0; i < page.Length; i++)
        {
            if (page[i] is { } slot && slot.Entry == entry)
                return i;
        }

        for (var i = 0; i < page.Length; i++)
        {
            if (page[i] != null)
                continue;

            page[i] = new WFMappingQuickSlotEntry(entry);
            added = true;
            return i;
        }

        return -1;
    }

    public void Remove(int slot)
    {
        if (slot >= 0 && slot < SlotsPerPage)
            CurrentPage[slot] = null;
    }

    public void Swap(int a, int b)
    {
        if (a < 0 || b < 0 || a >= SlotsPerPage || b >= SlotsPerPage)
            return;

        var page = CurrentPage;
        (page[a], page[b]) = (page[b], page[a]);
    }

    /// <summary>Moves a slot to the first empty slot of another page of the set; false when that page is full.</summary>
    public bool MoveToPage(int slot, int pageIndex)
    {
        var set = Current;
        if (slot < 0 || slot >= SlotsPerPage || pageIndex < 0 || pageIndex >= set.Pages.Count ||
            pageIndex == set.ActivePage || CurrentPage[slot] is not { } entry)
        {
            return false;
        }

        var target = set.Pages[pageIndex];
        for (var i = 0; i < target.Length; i++)
        {
            if (target[i] != null)
                continue;

            target[i] = entry;
            CurrentPage[slot] = null;
            return true;
        }

        return false;
    }

    /// <summary>Gives a slot a label, or clears it with an empty one.</summary>
    public void SetLabel(int slot, string? label)
    {
        if (slot < 0 || slot >= SlotsPerPage || CurrentPage[slot] is not { } entry)
            return;

        CurrentPage[slot] = entry with { Label = NormalizeName(label) };
    }

    /// <summary>Reads a store; null when the text isn't one. Bad slots read as empty, extras are dropped.</summary>
    public static WFMappingQuickSelectStore? Read(string text)
    {
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception)
        {
            return null;
        }

        if (root == null || Child(root, "sets") is not YamlSequenceNode sets)
            return null;

        var store = new WFMappingQuickSelectStore();
        foreach (var node in sets.Children)
        {
            if (store._sets.Count == MaxSets)
                break;

            if (node is not YamlMappingNode setNode)
                continue;

            var set = new WFMappingQuickSelectSet(NormalizeName(Scalar(setNode, "name")) ??
                                                  (store._sets.Count + 1).ToString(CultureInfo.InvariantCulture));
            if (Child(setNode, "pages") is YamlSequenceNode pages)
            {
                foreach (var pageNode in pages.Children)
                {
                    if (set.Pages.Count == MaxPages)
                        break;

                    set.Pages.Add(ReadPage(pageNode as YamlSequenceNode));
                }
            }

            if (set.Pages.Count == 0)
                set.Pages.Add(new WFMappingQuickSlotEntry?[SlotsPerPage]);

            set.ActivePage = Math.Clamp(Int(setNode, "activePage"), 0, set.Pages.Count - 1);
            store._sets.Add(set);
        }

        if (store._sets.Count == 0)
            return null;

        store._activeSet = Math.Clamp(Int(root, "activeSet"), 0, store._sets.Count - 1);
        store.AddPicks = Scalar(root, "addPicks") == "true";
        return store;
    }

    /// <summary>The store as YAML text.</summary>
    public string Write()
    {
        var root = new YamlMappingNode();
        root.Add("version", "1");
        root.Add("activeSet", _activeSet.ToString(CultureInfo.InvariantCulture));
        root.Add("addPicks", AddPicks ? "true" : "false");

        var sets = new YamlSequenceNode();
        foreach (var set in _sets)
        {
            var setNode = new YamlMappingNode();
            setNode.Add("name", set.Name);
            setNode.Add("activePage", set.ActivePage.ToString(CultureInfo.InvariantCulture));

            var pages = new YamlSequenceNode();
            foreach (var page in set.Pages)
            {
                var pageNode = new YamlSequenceNode();
                foreach (var slot in page)
                {
                    pageNode.Add(WriteSlot(slot));
                }

                pages.Add(pageNode);
            }

            setNode.Add("pages", pages);
            sets.Add(setNode);
        }

        root.Add("sets", sets);

        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, false);
        return writer.ToString();
    }

    private static WFMappingQuickSlotEntry?[] ReadPage(YamlSequenceNode? node)
    {
        var page = new WFMappingQuickSlotEntry?[SlotsPerPage];
        if (node == null)
            return page;

        var index = 0;
        foreach (var item in node.Children)
        {
            if (index == SlotsPerPage)
                break;

            page[index++] = ReadSlot(item as YamlMappingNode);
        }

        return page;
    }

    private static WFMappingQuickSlotEntry? ReadSlot(YamlMappingNode? node)
    {
        if (node == null ||
            Scalar(node, "id") is not { Length: > 0 and <= MaxIdLength } id ||
            ParseKind(Scalar(node, "kind")) is not { } kind)
        {
            return null;
        }

        Color? color = Scalar(node, "color") is { } hex && Color.TryFromHex(hex, out var parsed) ? parsed : null;
        return new WFMappingQuickSlotEntry(new WFMappingPaletteEntry(kind, id, color),
            NormalizeName(Scalar(node, "label")));
    }

    private static YamlMappingNode WriteSlot(WFMappingQuickSlotEntry? slot)
    {
        var node = new YamlMappingNode();
        if (slot is not { } entry)
            return node;

        node.Add("kind", KindName(entry.Entry.Kind));
        node.Add("id", entry.Entry.Id);
        if (entry.Entry.Color is { } color)
            node.Add("color", color.ToHex());

        if (entry.Label != null)
            node.Add("label", entry.Label);

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

    private static int Int(YamlMappingNode node, string key)
    {
        return Scalar(node, key) is { } text && Parse.TryInt32(text, out var value) ? value : 0;
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
