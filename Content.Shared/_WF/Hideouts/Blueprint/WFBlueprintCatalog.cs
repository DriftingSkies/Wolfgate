using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Decals;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Hideouts.Blueprint;

/// <summary>
/// A shelf of the blueprint's palette filled straight from the game's prototypes: every entity, floor tile or decal
/// listed becomes a brush under the shelf's heading, named as the prototype is, at the shelf's price.
/// </summary>
[Prototype("wfBlueprintCatalogue")]
public sealed partial class WFBlueprintCataloguePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The palette section its brushes are listed under.</summary>
    [DataField(required: true)]
    public LocId Group;

    [DataField]
    public int Order;

    /// <summary>What a tile of any of its brushes costs; an entity never costs less than one and a half times what it sells for.</summary>
    [DataField]
    public int Price;

    /// <summary>Whether its entities share a tile with whatever else stands there: rugs, and what hangs on a wall.</summary>
    [DataField]
    public bool Under;

    /// <summary>The colour its brushes are drawn in where they have no picture.</summary>
    [DataField]
    public Color Color = Color.FromHex("#8A8FA0");

    [DataField]
    public List<EntProtoId> Entities = new();

    [DataField]
    public List<ProtoId<ContentTileDefinition>> Tiles = new();

    [DataField]
    public List<ProtoId<DecalPrototype>> Decals = new();
}

/// <summary>One brush of the palette, from a brush prototype or a catalogue's shelf.</summary>
public sealed record WFBlueprintEntry(
    string Id,
    string Name,
    LocId Group,
    int Order,
    WFBlueprintKind Kind,
    string? Tile,
    string? Entity,
    string? Decal,
    int Price,
    bool Under,
    Color Color)
{
    /// <summary>The plan layer the brush paints on; a tile takes one stroke per layer.</summary>
    public int Layer => Kind switch
    {
        WFBlueprintKind.Floor => 0,
        WFBlueprintKind.Decal or WFBlueprintKind.CleanDecals => 1,
        WFBlueprintKind.Roof or WFBlueprintKind.Sky => 4,
        _ => Under ? 2 : 3,
    };

    /// <summary>How many plan layers there are.</summary>
    public const int Layers = 5;
}

/// <summary>The palette: every brush prototype and everything on a catalogue's shelves, by brush id.</summary>
public static class WFBlueprintCatalog
{
    /// <summary>What separates a catalogue's id from the prototype it lists, in a brush id.</summary>
    private const char Separator = ':';

    /// <summary>Every brush, in palette order.</summary>
    public static List<WFBlueprintEntry> All(IPrototypeManager protos, ITileDefinitionManager tiles)
    {
        var entries = new List<WFBlueprintEntry>();
        foreach (var brush in protos.EnumeratePrototypes<WFBlueprintBrushPrototype>())
        {
            entries.Add(FromBrush(brush));
        }

        foreach (var shelf in protos.EnumeratePrototypes<WFBlueprintCataloguePrototype>())
        {
            foreach (var entity in shelf.Entities)
            {
                if (protos.TryIndex(entity, out var proto))
                    entries.Add(FromShelf(shelf, entity.Id, proto.Name, WFBlueprintKind.Entity, entity: entity.Id));
            }

            foreach (var tile in shelf.Tiles)
            {
                if (tiles.TryGetDefinition(tile.Id, out var def))
                    entries.Add(FromShelf(shelf, tile.Id, Loc.GetString(def.Name), WFBlueprintKind.Floor, tile: tile.Id));
            }

            foreach (var decal in shelf.Decals)
            {
                if (protos.HasIndex(decal))
                    entries.Add(FromShelf(shelf, decal.Id, decal.Id, WFBlueprintKind.Decal, decal: decal.Id));
            }
        }

        return entries.OrderBy(entry => entry.Order).ThenBy(entry => entry.Name).ThenBy(entry => entry.Id).ToList();
    }

    /// <summary>The brush with an id: a brush prototype's, or a catalogue's and one of its prototypes'.</summary>
    public static bool TryGet(IPrototypeManager protos, ITileDefinitionManager tiles, string id, [NotNullWhen(true)] out WFBlueprintEntry? entry)
    {
        entry = null;
        var at = id.IndexOf(Separator);
        if (at < 0)
        {
            if (!protos.TryIndex<WFBlueprintBrushPrototype>(id, out var brush))
                return false;

            entry = FromBrush(brush);
            return true;
        }

        var item = id[(at + 1)..];
        if (!protos.TryIndex<WFBlueprintCataloguePrototype>(id[..at], out var shelf))
            return false;

        if (shelf.Entities.Any(listed => listed.Id == item) && protos.TryIndex<EntityPrototype>(item, out var proto))
            entry = FromShelf(shelf, item, proto.Name, WFBlueprintKind.Entity, entity: item);
        else if (shelf.Tiles.Any(listed => listed.Id == item) && tiles.TryGetDefinition(item, out var def))
            entry = FromShelf(shelf, item, Loc.GetString(def.Name), WFBlueprintKind.Floor, tile: item);
        else if (shelf.Decals.Any(listed => listed.Id == item) && protos.HasIndex<DecalPrototype>(item))
            entry = FromShelf(shelf, item, item, WFBlueprintKind.Decal, decal: item);

        return entry != null;
    }

    private static WFBlueprintEntry FromBrush(WFBlueprintBrushPrototype brush)
    {
        return new WFBlueprintEntry(brush.ID, Loc.GetString(brush.Name), brush.Group, brush.Order, brush.Kind, brush.Tile?.Id,
            brush.Entity?.Id, null, brush.Price, brush.Under, brush.Color);
    }

    private static WFBlueprintEntry FromShelf(WFBlueprintCataloguePrototype shelf, string item, string name, WFBlueprintKind kind,
        string? tile = null, string? entity = null, string? decal = null)
    {
        return new WFBlueprintEntry($"{shelf.ID}{Separator}{item}", name, shelf.Group, shelf.Order, kind, tile, entity, decal, shelf.Price,
            shelf.Under, shelf.Color);
    }
}
