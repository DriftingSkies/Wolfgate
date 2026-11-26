using Content.Shared.Contraband;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.SafetyDepositBox.Components;

/// <summary>
/// What a safety deposit box takes beyond its storage blacklist, which stays absolute: refused prototypes never,
/// restricted items only through the allow list, and reagents only of the listed groups or as the item spawns with them.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SafetyDepositAllowListComponent : Component
{
    /// <summary>
    /// Refused unless the item is allowed by <see cref="Prototypes"/>, <see cref="Stacks"/> or <see cref="Allowances"/>.
    /// </summary>
    [DataField(required: true)]
    public EntityWhitelist Restricted = new();

    /// <summary>
    /// Restricted items let in anyway, by exact prototype.
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> Prototypes = new();

    /// <summary>
    /// Restricted stacks let in anyway, by stack type.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<StackPrototype>> Stacks = new();

    /// <summary>
    /// Restricted items let in anyway, by what they are and how contraband they are.
    /// </summary>
    [DataField]
    public List<SafetyDepositAllowance> Allowances = new();

    /// <summary>
    /// Items always refused, whatever else lets them in: these prototypes and every prototype that inherits from them.
    /// </summary>
    [DataField]
    public List<SafetyDepositRefusal> Refused = new();

    /// <summary>
    /// Reagent groups an item's solutions may hold in any amount. Other reagents are only allowed up to what the
    /// item's prototype spawns with.
    /// </summary>
    [DataField]
    public HashSet<string> ReagentGroups = new();
}

/// <summary>
/// Restricted items that pass <see cref="Whitelist"/> and miss <see cref="Blacklist"/>: contraband of one of
/// <see cref="Severities"/>, or with none listed, items that aren't contraband.
/// </summary>
[DataDefinition]
public sealed partial class SafetyDepositAllowance
{
    /// <summary>What the guidebook calls these items.</summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();

    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>Contraband severities let in. Empty lets in only items that aren't contraband.</summary>
    [DataField]
    public HashSet<ProtoId<ContrabandSeverityPrototype>> Severities = new();
}

/// <summary>
/// A named group of prototypes, abstract ones included, that a box refuses along with everything inheriting from them.
/// </summary>
[DataDefinition]
public sealed partial class SafetyDepositRefusal
{
    /// <summary>What the guidebook calls these items.</summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public HashSet<EntProtoId> Prototypes = new();
}
