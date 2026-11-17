using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Materials;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Lathe;

/// <summary>
/// What a queued batch still needs before its next item can start, counting linked silos.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheMissingSupplies
{
    /// <summary>
    /// False once the lathe no longer has the design.
    /// </summary>
    public bool DesignAvailable = true;

    /// <summary>
    /// Missing material amounts.
    /// </summary>
    public Dictionary<ProtoId<MaterialPrototype>, int> Materials = new();

    /// <summary>
    /// Missing part counts.
    /// </summary>
    public Dictionary<EntProtoId, int> Entities = new();

    /// <summary>
    /// Missing reagent amounts.
    /// </summary>
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Reagents = new();

    /// <summary>
    /// Nothing is missing.
    /// </summary>
    public bool Ready => DesignAvailable && Materials.Count == 0 && Entities.Count == 0 && Reagents.Count == 0;
}

/// <summary>
/// Sets the requested total of a queued batch.
/// </summary>
[Serializable, NetSerializable]
public sealed class LatheRecipeAmountMessage(int index, int amount) : BoundUserInterfaceMessage
{
    /// <summary>
    /// Index of the batch.
    /// </summary>
    public readonly int Index = index;

    /// <summary>
    /// New requested total.
    /// </summary>
    public readonly int Amount = amount;
}
