using System.Linq;
using Content.Client.DisplacementMap;
using Content.Shared._WF.LegStyle;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client._WF.LegStyle;

/// <summary>
/// Fits clothing and leg markings to a character's picked legs.
/// </summary>
public sealed partial class LegStyleSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DisplacementMapSystem _displacement = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>The shader that reshapes a leg marking and cuts it to its own leg.</summary>
    private const string MarkingShader = "WFLegMarkingDisplaced";

    /// <summary>
    /// The displacement map a clothing slot uses on this wearer, given the one its species would use.
    /// </summary>
    public DisplacementData? GetDisplacement(EntityUid wearer, string slot, DisplacementData? species)
    {
        if (!TryComp<HumanoidAppearanceComponent>(wearer, out var humanoid)
            || humanoid.LegStyle is not { } id
            || !_proto.TryIndex(id, out var style)
            || !style.DisplacementSlots.Contains(slot))
        {
            return species;
        }

        if (humanoid.Sex == Sex.Female && style.FemaleDisplacements.TryGetValue(slot, out var female))
            return female;

        return style.Displacements.GetValueOrDefault(slot);
    }

    /// <summary>
    /// Reshapes a newly added leg or foot marking layer on digitigrade legs with the map its clothing uses, as most
    /// markings are drawn for plantigrade legs. Art the style lists as drawn for its legs is left alone.
    /// </summary>
    public void FitMarking(Entity<HumanoidAppearanceComponent, SpriteComponent> ent, HumanoidVisualLayers part, string layerId, ResPath rsi)
    {
        var slot = part switch
        {
            HumanoidVisualLayers.LLeg or HumanoidVisualLayers.RLeg => "jumpsuit",
            HumanoidVisualLayers.LFoot or HumanoidVisualLayers.RFoot => "shoes",
            _ => null,
        };

        // Marking sprites keep the path as written, which may leave out the texture root.
        var path = rsi.IsRooted ? rsi : SpriteSpecifierSerializer.TextureRoot / rsi;
        if (slot is not { } fitSlot
            || ent.Comp1.LegStyle is not { } id
            || !_proto.TryIndex(id, out var style)
            || style.Stance != LegStance.Digitigrade
            || style.FittedMarkings.Any(folder => path.TryRelativeTo(SpriteSpecifierSerializer.TextureRoot / folder, out _))
            || GetDisplacement(ent, fitSlot, null) is not { } data
            || !ent.Comp2.LayerMapTryGet(layerId, out var index))
        {
            return;
        }

        // A clothing map pulls both legs of a side view from where the one plantigrade leg stood, so the marking is
        // cut to its own leg and foot. Without both outlines it is only reshaped.
        var (leg, foot) = part is HumanoidVisualLayers.LLeg or HumanoidVisualLayers.LFoot
            ? (HumanoidVisualLayers.LLeg, HumanoidVisualLayers.LFoot)
            : (HumanoidVisualLayers.RLeg, HumanoidVisualLayers.RFoot);
        var legMask = Outline(ent.Comp2, leg, layerId, "legMask");
        var footMask = Outline(ent.Comp2, foot, layerId, "footMask");
        if (legMask != null && footMask != null)
            data = new DisplacementData { SizeMaps = data.SizeMaps, ShaderOverride = MarkingShader };

        if (!_displacement.TryAddDisplacement(data, ent.Comp2, index, layerId, new HashSet<string>()))
            return;

        ent.Comp1.ClientMarkingLayers.Add($"{layerId}-displacement");
        if (legMask == null || footMask == null)
            return;

        // Shader-parameter layers sit below the layer they feed.
        foreach (var (mask, key) in new[] { (legMask, $"{layerId}-legmask"), (footMask, $"{layerId}-footmask") })
        {
            if (!ent.Comp2.LayerMapTryGet(layerId, out var target))
                return;

            _sprite.AddLayer((ent.Owner, ent.Comp2), mask, target);
            _sprite.LayerMapSet((ent.Owner, ent.Comp2), key, target);
            ent.Comp1.ClientMarkingLayers.Add(key);
        }
    }

    /// <summary>
    /// A layer that hands a body part's current sprite to a marking's shader as an outline, or null if the part
    /// draws no sprite.
    /// </summary>
    private PrototypeLayerData? Outline(SpriteComponent sprite, HumanoidVisualLayers part, string layerId, string parameter)
    {
        if (!sprite.LayerMapTryGet(part, out var index)
            || sprite.LayerGetActualRSI(index) is not { } rsi
            || sprite[index] is not SpriteComponent.Layer { State.Name: { } state })
        {
            return null;
        }

        return new PrototypeLayerData
        {
            RsiPath = rsi.Path.ToString(),
            State = state,
            CopyToShaderParameters = new PrototypeCopyToShaderParameters
            {
                LayerKey = layerId,
                ParameterTexture = parameter,
                ParameterUV = parameter + "UV",
            },
        };
    }
}
