namespace Content.Shared._WG.Augments;

/// <summary>Where on the body an augment goes; a character holds at most one augment per slot.</summary>
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
}

/// <summary>What an augment does to its slot.</summary>
public enum WGAugmentKind : byte
{
    /// <summary>Plating drawn over the slot in a style; the part itself is unchanged (head, chest).</summary>
    Plating,

    /// <summary>The limb is replaced with <see cref="WGAugmentPrototype.Part"/>.</summary>
    Limb,

    /// <summary>The limb is taken off and nothing replaces it.</summary>
    Missing,

    /// <summary>The organ in the slot is replaced with <see cref="WGAugmentPrototype.Organ"/>.</summary>
    Organ,
}
