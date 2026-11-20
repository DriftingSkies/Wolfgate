using Content.Shared._WG.Augments;
using Content.Shared._WG.LegStyle;
using Content.Shared.Body.Components;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Augments;

public sealed partial class WGAugmentSystem
{
    [Dependency] private SharedHumanoidAppearanceSystem _humanoid = default!;

    /// <summary>The style limbs fitted by a trait (Prybar Prosthetics, Bionic Legs) are drawn in: the bishop chassis they always had.</summary>
    public static readonly ProtoId<WGAugmentStylePrototype> TraitLimbStyle = "WGAugmentStyleEEBishop";

    /// <summary>
    /// Draws the limbs a trait fitted in <see cref="TraitLimbStyle"/>, fitted to the body's own limb shape, so an
    /// Avali's prosthetic arm keeps the Avali outline instead of the human one.
    /// </summary>
    public void StyleTraitLimbs(EntityUid body, params WGAugmentSlot[] slots)
    {
        if (!TryComp<BodyComponent>(body, out var bodyComp)
            || !TryComp<HumanoidAppearanceComponent>(body, out var humanoid)
            || !_proto.TryIndex(TraitLimbStyle, out var style))
            return;

        var digitigrade = humanoid.LegStyle is { } legStyle
                          && _proto.TryIndex(legStyle, out var legProto)
                          && legProto.Stance == LegStance.Digitigrade;

        foreach (var slot in slots)
        {
            if (FindLimb((body, bodyComp), slot) is not { } limb)
                continue;

            foreach (var layer in WGAugmentRules.Layers(slot))
            {
                var under = WGAugmentRules.Underlying(humanoid, layer, _proto);
                if (WGAugmentRules.Sprite(style, layer, digitigrade, under, _proto) is not { } sprite)
                    continue;

                // White, so a layer drawn before in skin tint doesn't keep it.
                var info = new CustomBaseLayerInfo(sprite, Color.White);
                humanoid.CustomBaseLayers[layer] = info;
                humanoid.AugmentLayers[layer] = info;
            }

            StyleLimb(body, limb.Id, style, digitigrade);
        }

        Dirty(body, humanoid);
    }

    /// <summary>
    /// Puts the profile's augments back after its traits ran. A trait that fits its own limbs deletes the old ones with
    /// the hands, feet and implants on them; installing again only adds what is missing.
    /// </summary>
    internal void ReapplyOverTraits(EntityUid body, HumanoidCharacterProfile profile)
    {
        if (WGAugmentRules.ClaimedSlots(profile.TraitPreferences, _proto).Count == 0
            || !TryComp<HumanoidAppearanceComponent>(body, out var humanoid))
            return;

        var ev = new WGApplyAugmentsEvent(profile);
        RaiseLocalEvent(body, ref ev);
        _humanoid.ApplyAugmentVisuals(body, profile, humanoid, keepUnpicked: true);
    }
}
