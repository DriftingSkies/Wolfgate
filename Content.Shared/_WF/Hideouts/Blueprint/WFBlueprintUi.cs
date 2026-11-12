using Content.Shared.Maps;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Hideouts.Blueprint;

/// <summary>What a blueprint brush does to the tile it is painted on.</summary>
public enum WFBlueprintKind : byte
{
    /// <summary>Lays its floor tile, over the outpost's floor or out onto open ground in its claim.</summary>
    Floor,

    /// <summary>Sets its entity on a floor tile nothing else stands on.</summary>
    Entity,

    /// <summary>Roofs the tile over.</summary>
    Roof,

    /// <summary>Opens the tile to the sky.</summary>
    Sky,

    /// <summary>Takes away what a brush set on the tile.</summary>
    Erase,

    /// <summary>Lays its decal on the floor of the tile, turned the way the plan faces.</summary>
    Decal,

    /// <summary>Takes the decals off the tile.</summary>
    CleanDecals,
}

/// <summary>One brush on the outpost console's Blueprint tab.</summary>
[Prototype("wfBlueprintBrush")]
public sealed partial class WFBlueprintBrushPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name;

    /// <summary>The palette section it is listed under.</summary>
    [DataField(required: true)]
    public LocId Group;

    [DataField]
    public int Order;

    [DataField(required: true)]
    public WFBlueprintKind Kind;

    [DataField]
    public ProtoId<ContentTileDefinition>? Tile;

    [DataField]
    public EntProtoId? Entity;

    /// <summary>What a tile of it costs; an entity brush never costs less than one and a half times what its entity sells for.</summary>
    [DataField]
    public int Price;

    /// <summary>Whether its entity lies under whatever else stands on the tile, as a cable or a carpet does.</summary>
    [DataField]
    public bool Under;

    /// <summary>The colour it is drawn in on the plan.</summary>
    [DataField]
    public Color Color = Color.FromHex("#8A8FA0");
}

/// <summary>Limits shared by the Blueprint tab and the server.</summary>
public static class WFBlueprintLimits
{
    /// <summary>The most strokes one plan may hold.</summary>
    public const int Strokes = 800;
}

/// <summary>What stands on a tile of the outpost, as the plan draws it.</summary>
[Serializable, NetSerializable]
public enum WFBlueprintCellKind : byte
{
    Wall,
    Window,
    Door,
    Thing,
}

[Serializable, NetSerializable]
public record struct WFBlueprintCell(Vector2i Index, WFBlueprintCellKind Kind);

/// <summary>One painted tile of a plan: the brush and, for an entity, the way it faces.</summary>
[Serializable, NetSerializable]
public record struct WFBlueprintStroke(Vector2i Index, string Brush, Direction Facing);

/// <summary>The Blueprint tab asks what stands on the outpost.</summary>
[Serializable, NetSerializable]
public sealed class WFBlueprintCanvasRequestMessage : BoundUserInterfaceMessage;

/// <summary>What stands on the outpost, each brush's price and the builder's balance.</summary>
[Serializable, NetSerializable]
public sealed class WFBlueprintCanvasMessage(List<WFBlueprintCell> cells, Dictionary<string, int> prices, int balance) : BoundUserInterfaceMessage
{
    public readonly List<WFBlueprintCell> Cells = cells;
    public readonly Dictionary<string, int> Prices = prices;
    public readonly int Balance = balance;
}

/// <summary>A builder has the plan built, paid from their bank.</summary>
[Serializable, NetSerializable]
public sealed class WFBlueprintApplyMessage(List<WFBlueprintStroke> strokes) : BoundUserInterfaceMessage
{
    public readonly List<WFBlueprintStroke> Strokes = strokes;
}
