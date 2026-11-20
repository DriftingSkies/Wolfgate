namespace Content.Shared._WG.Augments;

/// <summary>
/// Where on the body an augment goes; a character holds at most one limb, organ or plating per slot, and implants in
/// the slot's part up to what it holds. Augments are applied in this order, so a limb comes before the hand or foot
/// on it. Stored by name.
/// </summary>
public enum WGAugmentSlot : byte
{
    Head,
    Chest,
    LeftArm,
    RightArm,
    LeftLeg,
    RightLeg,
    Eyes,
    Heart,
    Lungs,
    Liver,
    Stomach,
    LeftHand,
    RightHand,
    LeftFoot,
    RightFoot,
}

/// <summary>What an augment does to its slot.</summary>
public enum WGAugmentKind : byte
{
    /// <summary>
    /// The slot's part converted in place: drawn in a style, and counted as synthetic for implants (head, chest). It
    /// can hold more implants than the natural part, never fewer.
    /// </summary>
    Plating,

    /// <summary>The limb is replaced with <see cref="WGAugmentPrototype.Part"/>.</summary>
    Limb,

    /// <summary>The limb is taken off and nothing replaces it.</summary>
    Missing,

    /// <summary>The organ in the slot is replaced with <see cref="WGAugmentPrototype.Organ"/>.</summary>
    Organ,

    /// <summary>
    /// An implant in the slot's part: it takes the upgrade <see cref="WGAugmentPrototype.Autosurgeon"/> installs.
    /// Several fit one part, up to its <see cref="WGAugmentPrototype.ImplantSlots"/>.
    /// </summary>
    Upgrade,
}

/// <summary>The parts an implant goes in.</summary>
public enum WGAugmentHost : byte
{
    Any,
    Organic,
    Synthetic,
}
