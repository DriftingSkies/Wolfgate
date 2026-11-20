using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Shared._WG.AdminObserver;

namespace Content.Server._WG.AdminObserver;

/// <summary>Reads, writes and cleans up <see cref="WGAdminObserverData"/> as the database stores it.</summary>
public static class WGAdminObserverSerializer
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The document as JSON. False when it is over <see cref="WGAdminObserverData.MaxBytes"/>.</summary>
    public static bool TrySerialize(WGAdminObserverData data, out string json)
    {
        json = JsonSerializer.Serialize(data, Json);
        return Encoding.UTF8.GetByteCount(json) <= WGAdminObserverData.MaxBytes;
    }

    /// <summary>A document from the database, or null when it can't be read or is from a newer version.</summary>
    public static WGAdminObserverData? Deserialize(string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<WGAdminObserverData>(json, Json);
            return data is { Version: >= 1 and <= WGAdminObserverData.CurrentVersion } ? data : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Brings a document within its limits and drops what names a prototype that no longer exists: avatars, outfits
    /// and kit items. Returns the same document.
    /// </summary>
    public static WGAdminObserverData Sanitize(
        WGAdminObserverData data,
        Func<string, bool> entityExists,
        Func<string, bool> gearExists)
    {
        data.Version = WGAdminObserverData.CurrentVersion;
        data.Presets ??= new();
        data.Marker ??= new();

        var names = new HashSet<string>();
        var presets = new List<WGAvatarPreset>();
        foreach (var preset in data.Presets)
        {
            if (preset == null || presets.Count >= WGAdminObserverData.MaxPresets)
                continue;

            preset.Name = Clamp(preset.Name, WGAdminObserverData.MaxNameLength).Trim();
            if (preset.Name.Length == 0 || names.Contains(preset.Name))
                continue;

            if (!Enum.IsDefined(preset.Kind) || !Enum.IsDefined(preset.EntryStyle))
                continue;

            if (preset.Kind is WGAvatarKind.Object or WGAvatarKind.Mob)
            {
                if (preset.Prototype == null || !entityExists(preset.Prototype))
                    continue;
            }
            else
            {
                preset.Prototype = null;
            }

            if (preset.Kind != WGAvatarKind.Character)
            {
                preset.CharacterSlot = null;
                preset.Outfit = null;
                preset.OutfitLayout = null;
            }

            if (preset.Outfit != null && !gearExists(preset.Outfit))
                preset.Outfit = null;

            if (preset.OutfitLayout != null)
                SanitizeLayout(preset.OutfitLayout, entityExists);

            preset.DisplayName = Clamp(preset.DisplayName, WGAdminObserverData.MaxDisplayNameLength);
            preset.Description = Clamp(preset.Description, WGAdminObserverData.MaxDescriptionLength);
            names.Add(preset.Name);
            presets.Add(preset);
        }

        data.Presets = presets;
        if (data.DefaultPreset != null && !names.Contains(data.DefaultPreset))
            data.DefaultPreset = null;

        var marker = data.Marker;
        if (!Enum.IsDefined(marker.Wording))
            marker.Wording = WGMarkerWording.Rank;

        marker.Visuals &= AllVisuals;
        if (marker.Visuals == WGMarkerVisuals.None)
            marker.Visuals = WGMarkerVisuals.Nameplate;

        marker.Ring = Clamp(marker.Ring, WGAdminObserverData.MaxNameLength);
        marker.Glyph = Clamp(marker.Glyph, WGAdminObserverData.MaxNameLength);
        marker.Shield = Clamp(marker.Shield, WGAdminObserverData.MaxNameLength);
        marker.Aura = Clamp(marker.Aura, WGAdminObserverData.MaxNameLength);
        if (marker.Color != null && !Color.TryFromHex(marker.Color, out _))
            marker.Color = null;

        if (data.Kit != null)
            SanitizeLayout(data.Kit, entityExists);

        return data;
    }

    private static readonly WGMarkerVisuals AllVisuals = Enum.GetValues<WGMarkerVisuals>()
        .Aggregate(WGMarkerVisuals.None, (all, flag) => all | flag);

    /// <summary>Drops a layout's unknown items with their contents, and whatever is past the depth and count limits.</summary>
    public static void SanitizeLayout(WGKitLayout layout, Func<string, bool> entityExists)
    {
        var budget = WGAdminObserverData.MaxKitNodes;
        layout.Slots = Prune(layout.Slots, entityExists, ref budget);
        layout.Hands = Prune(layout.Hands, entityExists, ref budget);
    }

    private static Dictionary<string, WGKitNode> Prune(
        Dictionary<string, WGKitNode>? nodes,
        Func<string, bool> entityExists,
        ref int budget)
    {
        var kept = new Dictionary<string, WGKitNode>();
        if (nodes == null)
            return kept;

        foreach (var (key, node) in nodes)
        {
            if (PruneNode(node, entityExists, 1, ref budget))
                kept[key] = node;
        }

        return kept;
    }

    private static bool PruneNode(WGKitNode? node, Func<string, bool> entityExists, int depth, ref int budget)
    {
        if (node == null || budget <= 0 || depth > WGAdminObserverData.MaxKitDepth
            || string.IsNullOrEmpty(node.Prototype) || !entityExists(node.Prototype))
        {
            return false;
        }

        budget--;
        var contents = new List<WGKitStored>();
        foreach (var stored in node.Contents ?? new())
        {
            if (stored != null && PruneNode(stored.Node, entityExists, depth + 1, ref budget))
                contents.Add(stored);
        }

        node.Contents = contents;
        return true;
    }

    private static string Clamp(string? text, int length)
    {
        if (text == null)
            return string.Empty;

        return text.Length <= length ? text : text[..length];
    }
}
