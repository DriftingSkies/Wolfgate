using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.SafetyDepositBox.Components;

/// <summary>
/// Items a safety deposit box refuses unless they are on its allow list. The storage blacklist stays absolute.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SafetyDepositAllowListComponent : Component
{
    /// <summary>
    /// Refused unless the item is allowed by <see cref="Prototypes"/> or <see cref="Stacks"/>.
    /// </summary>
    [DataField(required: true)]
    public EntityWhitelist Restricted = new();

    /// <summary>
    /// Restricted items let in anyway, by exact prototype. They are stored bare: anything inside them but their
    /// attached parts, such as a hardsuit's helmet, is handed back on deposit.
    /// </summary>
    [DataField]
    public HashSet<EntProtoId> Prototypes = new();

    /// <summary>
    /// Restricted stacks let in anyway, by stack type.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<StackPrototype>> Stacks = new();
}
