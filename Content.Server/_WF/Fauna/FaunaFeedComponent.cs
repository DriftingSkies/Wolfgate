using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Feeding the mob a food item splits off a copy, and feeding it an evolve key turns it into
/// <see cref="EvolveInto"/>. Items match by whitelist or by stack type.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaFeedComponent : Component
{
    /// <summary>
    /// Items that count as food.
    /// </summary>
    [DataField]
    public EntityWhitelist? Food;

    /// <summary>
    /// Stack types that count as food.
    /// </summary>
    [DataField]
    public List<ProtoId<StackPrototype>> FoodStacks = new();

    /// <summary>
    /// Items that count as an evolve key.
    /// </summary>
    [DataField]
    public EntityWhitelist? EvolveKey;

    /// <summary>
    /// Stack types that count as an evolve key.
    /// </summary>
    [DataField]
    public List<ProtoId<StackPrototype>> EvolveStacks = new();

    /// <summary>
    /// Mob this one turns into when fed an evolve key.
    /// </summary>
    [DataField]
    public EntProtoId? EvolveInto;
}
