using Content.Shared.Humanoid.Markings;
using Robust.Shared.Serialization;

namespace Content.Shared.Humanoid
{
    [Serializable, NetSerializable]
    public enum HumanoidVisualLayers : byte
    {
        Special, // for the cat ears
        // WOLFGATE(Genitals) START: underwear and genital layers, ported from HardLight
        Genital,
        Penis,
        Breasts,
        UndergarmentTop,
        UndergarmentBottom,
        // WOLFGATE END
        TailExtras,
        Tail,
        Wings, // For IPC wings porting from SimpleStation
        Hair,
        FacialHair,
        Chest,
        Head,
        Snout,
        HeadSide, // side parts (i.e., frills)
        HeadTop,  // top parts (i.e., ears)
        // WOLFGATE(Genitals) START: split tail layers, ported from HardLight/Floof
        // A tail can sit behind the mob from most angles and over the suit when facing north, instead of being
        // cookie-cut.
        TailBehind,
        TailOversuit,
        // WOLFGATE END
        Eyes,
        RArm,
        LArm,
        RHand,

        LHand,
        RLeg,
        LLeg,
        RFoot,
        LFoot,
        Handcuffs,
        StencilMask,
        Ensnare,
        Fire,
        LArmExtension, // Frontier: a species-specific extension layer, e.g. for harpy wings
        RArmExtension, // Frontier: a species-specific extension layer, e.g. for harpy wings
        UndergarmentSocks, // WOLFGATE(Humanoid): add a character-creator layer for socks

    }
}
