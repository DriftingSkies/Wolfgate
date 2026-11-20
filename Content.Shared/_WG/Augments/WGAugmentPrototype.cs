using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._WG.Augments;

/// <summary>
/// A piece of cyberware a character can carry from round to round: picked or bought in the creator, or installed in
/// a round and kept. Every cybernetic part, organ and autosurgeon upgrade the game has is listed as one.
/// </summary>
[Prototype("wgAugment")]
public sealed partial class WGAugmentPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The name in the creator; the installed entity's name if left out.</summary>
    [DataField]
    public LocId? Name;

    /// <summary>The creator's tooltip; the installed entity's description if left out.</summary>
    [DataField]
    public LocId? Description;

    /// <summary>The creator's icon; the installed entity's sprite if left out.</summary>
    [DataField]
    public SpriteSpecifier? Icon;

    [DataField(required: true)]
    public WGAugmentSlot Slot;

    [DataField(required: true)]
    public WGAugmentKind Kind;

    /// <summary>The limb that replaces the slot's part, for <see cref="WGAugmentKind.Limb"/>.</summary>
    [DataField]
    public EntProtoId? Part;

    /// <summary>The organ that replaces the slot's organ, for <see cref="WGAugmentKind.Organ"/>.</summary>
    [DataField]
    public EntProtoId? Organ;

    /// <summary>The upgrading autosurgeon whose components it adds, for <see cref="WGAugmentKind.Upgrade"/>.</summary>
    [DataField]
    public EntProtoId? Autosurgeon;

    /// <summary>Whether an implant needs a flesh or a synthetic part, for <see cref="WGAugmentKind.Upgrade"/>.</summary>
    [DataField]
    public WGAugmentHost Host;

    /// <summary>How many implants the limb or plating holds, for <see cref="WGAugmentKind.Limb"/> and
    /// <see cref="WGAugmentKind.Plating"/>.</summary>
    [DataField]
    public int ImplantSlots = 1;

    /// <summary>Whether the player picks a style for it. Limbs and plating are styled.</summary>
    [DataField]
    public bool Styled;

    /// <summary>
    /// One-time price in spesos, taken from the character's bank the first time it is saved with this augment.
    /// Zero for augments that give no advantage. Taking it off in the creator refunds
    /// <see cref="WGAugmentRules.RefundPercent"/> of it if it was paid for. Scavenged loot is weighted by its inverse;
    /// free ones never drop.
    /// </summary>
    [DataField]
    public int Price;

    /// <summary>
    /// Implants that share a group can't be fitted to the same body, even in different parts, for
    /// <see cref="WGAugmentKind.Upgrade"/>.
    /// </summary>
    [DataField]
    public string? Exclusive;

    /// <summary>Species that can't take it.</summary>
    [DataField]
    public List<ProtoId<SpeciesPrototype>> SpeciesBlacklist = new();

    /// <summary>Sort order within the slot, lowest first.</summary>
    [DataField]
    public int Order;
}
