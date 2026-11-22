using Content.Client._WG.CustomMarkings;
using Content.Client._WG.LegStyle;
using Content.Shared._WG.MarkingGlow;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client.Humanoid;

public sealed partial class HumanoidAppearanceSystem
{
    private static readonly ProtoId<ShaderPrototype> Displaced = "DisplacedDraw";
    private static readonly ProtoId<ShaderPrototype> DisplacedGlow = "WGMarkingGlowDisplaced";
    private static readonly ProtoId<ShaderPrototype> LegDisplaced = LegStyleSystem.MarkingShader;
    private static readonly ProtoId<ShaderPrototype> LegDisplacedGlow = "WGMarkingGlowLegDisplaced";

    /// <summary>Whether a body's marking is drawn unshaded because its player made it glow.</summary>
    private bool MarkingGlows(HumanoidAppearanceComponent humanoid, MarkingPrototype marking)
    {
        return MarkingGlowEnabled && MarkingGlowRules.Glows(humanoid.MarkingGlow, marking);
    }

    /// <summary>
    /// Draws a glowing marking's layer unshaded. A layer a displacement map reshapes, as a leg marking on digitigrade
    /// legs is, takes that shader's unshaded twin and keeps its shape.
    /// </summary>
    private void ApplyMarkingGlow(SpriteComponent sprite, string layerId)
    {
        if (!sprite.LayerMapTryGet(layerId, out var index) || sprite[index] is not SpriteComponent.Layer layer)
            return;

        var shader = layer.ShaderPrototype;
        if (shader == DisplacedGlow || shader == LegDisplacedGlow)
            return;

        var glow = shader == Displaced ? DisplacedGlow : shader == LegDisplaced ? LegDisplacedGlow : SpriteSystem.UnshadedId;
        sprite.LayerSetShader(index, glow.Id);
    }

    /// <summary>
    /// Draws the eyes unshaded while they glow, and lit again once they stop. Erased eyes carry the erase shader's
    /// unshaded twin instead; the custom markings put the matching mask back on once the markings are applied.
    /// </summary>
    private void ApplyEyeGlow(HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        var index = sprite.LayerMapReserveBlank(HumanoidVisualLayers.Eyes);
        if (MarkingGlowEnabled && humanoid.MarkingGlow.Contains(MarkingGlowRules.Eyes))
        {
            sprite.LayerSetShader(index, SpriteSystem.UnshadedId.Id);
            return;
        }

        if (sprite[index] is SpriteComponent.Layer layer
            && (layer.ShaderPrototype == SpriteSystem.UnshadedId || layer.ShaderPrototype == CustomMarkingSystem.EraseShaderUnshaded))
            sprite.LayerSetShader(index, (ShaderInstance?) null);
    }
}
