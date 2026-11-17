using Content.Shared._WG.Lathe;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared.Lathe;

public sealed partial class LatheUpdateState
{
    /// <summary>
    /// Recipes whose next item can start now.
    /// </summary>
    public HashSet<ProtoId<LatheRecipePrototype>> ReadyRecipes = new();

    /// <summary>
    /// What each queued batch still needs, by batch index.
    /// </summary>
    public Dictionary<int, LatheMissingSupplies> QueueSupplies = new();

    /// <summary>
    /// Index of the batch the item being printed came from.
    /// </summary>
    public int? PrintingBatch;
}
