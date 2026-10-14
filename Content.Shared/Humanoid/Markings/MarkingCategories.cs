using Robust.Shared.Serialization;

namespace Content.Shared.Humanoid.Markings
{
    [Serializable, NetSerializable]
    public enum MarkingCategories : byte
    {
        Special,
        Hair,
        FacialHair,
        Head,
        HeadTop,
        HeadSide,
        Snout,
        Chest,
        // WOLFGATE(Genitals) START: underwear and genital marking slots, ported from HardLight
        UndergarmentTop,
        UndergarmentBottom,
        Genital,
        // WOLFGATE END
        Arms,
        Legs,
        Wings, // For IPC wings porting from SimpleStation
        Tail,
        TailExtras, // Starlight - Resomi Extra Tail Customisation
        // WOLFGATE(Species) START: eye markings get their own points instead of sharing the head's
        Overlay,
        Eyes,
        // WOLFGATE END
        Chassis, // WOLFGATE(Species): IPC body parts get their own points instead of filling the body categories
    }

    public static class MarkingCategoriesConversion
    {
        public static MarkingCategories FromHumanoidVisualLayers(HumanoidVisualLayers layer)
        {
            return layer switch
            {
                HumanoidVisualLayers.Special => MarkingCategories.Special,
                HumanoidVisualLayers.Hair => MarkingCategories.Hair,
                HumanoidVisualLayers.FacialHair => MarkingCategories.FacialHair,
                HumanoidVisualLayers.Head => MarkingCategories.Head,
                HumanoidVisualLayers.HeadTop => MarkingCategories.HeadTop,
                HumanoidVisualLayers.HeadSide => MarkingCategories.HeadSide,
                HumanoidVisualLayers.Snout => MarkingCategories.Snout,
                HumanoidVisualLayers.Chest => MarkingCategories.Chest,
                // WOLFGATE(Genitals) START: underwear and genital marking slots, ported from HardLight
                HumanoidVisualLayers.UndergarmentTop => MarkingCategories.UndergarmentTop,
                HumanoidVisualLayers.UndergarmentBottom => MarkingCategories.UndergarmentBottom,
                HumanoidVisualLayers.Genital => MarkingCategories.Genital,
                HumanoidVisualLayers.Penis => MarkingCategories.Genital,
                HumanoidVisualLayers.Breasts => MarkingCategories.Genital,
                // WOLFGATE END
                HumanoidVisualLayers.RArm => MarkingCategories.Arms,
                HumanoidVisualLayers.LArm => MarkingCategories.Arms,
                HumanoidVisualLayers.RHand => MarkingCategories.Arms,
                HumanoidVisualLayers.LHand => MarkingCategories.Arms,
                HumanoidVisualLayers.LLeg => MarkingCategories.Legs,
                HumanoidVisualLayers.RLeg => MarkingCategories.Legs,
                HumanoidVisualLayers.LFoot => MarkingCategories.Legs,
                HumanoidVisualLayers.RFoot => MarkingCategories.Legs,
                HumanoidVisualLayers.Wings => MarkingCategories.Wings, //For IPC wings porting from SimpleStation
                HumanoidVisualLayers.TailExtras => MarkingCategories.TailExtras, // Starlight - Resomi Extra Tail Customisation
                HumanoidVisualLayers.Tail => MarkingCategories.Tail,
                HumanoidVisualLayers.RArmExtension => MarkingCategories.Arms, // Frontier: species-specific layer
                HumanoidVisualLayers.LArmExtension => MarkingCategories.Arms, // Frontier: species-specific layer
                HumanoidVisualLayers.Eyes => MarkingCategories.Eyes, // WOLFGATE(Species): eye markings get their own category
                _ => MarkingCategories.Overlay
            };
        }
    }
}
