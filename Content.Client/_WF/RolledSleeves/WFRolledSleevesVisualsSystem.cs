using System.Diagnostics.CodeAnalysis;
using Content.Client.Clothing;
using Content.Shared._WF.RolledSleeves;
using Content.Shared.Clothing;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Serialization.TypeSerializers.Implementations;
using Robust.Shared.Utility;

namespace Content.Client._WF.RolledSleeves;

/// <summary>
/// Restyles a rolled jumpsuit at draw time: its worn layers get the rolled-sleeves shader, fed a mask built from the
/// human body silhouette. Works on any jumpsuit sprite without drawn rolled states.
/// </summary>
public sealed partial class WFRolledSleevesVisualsSystem : EntitySystem
{
    [Dependency] private IResourceCache _cache = default!;
    [Dependency] private ClientClothingSystem _clothing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string Shader = "WFRolledSleeves";
    private const string DisplacedShader = "WFRolledSleevesDisplaced";
    private const string MaskSuffix = "-wf-rolled-sleeves";
    private const string DisplacementSuffix = "-displacement";
    private const int MaskSize = 32;

    private static readonly ResPath MaskRsi = new("/Textures/_WF/RolledSleeves/sleeve_mask.rsi");
    private static readonly ResPath DrawnRoot = new("/Textures/_WF/RolledSleeves/Meridian");

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFRolledSleevesComponent, EquipmentVisualsUpdatedEvent>(OnVisualsUpdated);
        SubscribeLocalEvent<WFRolledSleevesComponent, AfterAutoHandleStateEvent>(OnAfterState);
        SubscribeLocalEvent<WFRolledSleevesComponent, WFJumpsuitStyleChangedEvent>(OnStyleChanged);
    }

    private void OnAfterState(Entity<WFRolledSleevesComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        Redraw(ent);
    }

    private void OnStyleChanged(Entity<WFRolledSleevesComponent> ent, ref WFJumpsuitStyleChangedEvent args)
    {
        Redraw(ent);
    }

    /// <summary>
    /// Redraws the wearer's clothes. Not through the item system: that needs the suit's InSlot, which the client
    /// never sets when it first sees the wearer already dressed.
    /// </summary>
    private void Redraw(EntityUid suit)
    {
        if (_inventory.TryGetContainingEntity(suit, out var wearer) && TryComp(wearer, out InventoryComponent? inventory))
            _clothing.InitClothing(wearer.Value, inventory);
    }

    private void OnVisualsUpdated(Entity<WFRolledSleevesComponent> ent, ref EquipmentVisualsUpdatedEvent args)
    {
        var maskState = ent.Comp.Style switch
        {
            WFJumpsuitStyle.SleevesRolled => "sleeves",
            WFJumpsuitStyle.RolledDown => "down",
            _ => null,
        };

        if (maskState == null || !TryComp(args.Equipee, out SpriteComponent? sprite))
            return;

        // States drawn for the wearer's species have their own arms; the mask fits the human body.
        var speciesSuffix = CompOrNull<InventoryComponent>(args.Equipee)?.SpeciesId is { } species ? $"-{species}" : null;

        // Copied: mask keys are added to the set while walking it.
        foreach (var key in new List<string>(args.RevealedLayers))
        {
            if (key.EndsWith(DisplacementSuffix) || key.EndsWith(MaskSuffix))
                continue;

            if (!_sprite.LayerMapTryGet((args.Equipee, sprite), key, out var index, false) ||
                sprite[index] is not SpriteComponent.Layer layer ||
                layer.State.Name is not { } state ||
                (speciesSuffix != null && state.EndsWith(speciesSuffix)) ||
                sprite.LayerGetActualRSI(index) is not { } rsi ||
                rsi.Size.X != MaskSize || rsi.Size.Y != MaskSize)
            {
                continue;
            }

            // Suits Meridian Rift drew a rolled-down sprite for use it instead of the cut.
            if (ent.Comp.Style == WFJumpsuitStyle.RolledDown && TryGetDrawnRollDown(rsi, state, out var drawn))
            {
                _sprite.LayerSetRsi((args.Equipee, sprite), index, drawn, state);
                continue;
            }

            var displaced = args.RevealedLayers.Contains(key + DisplacementSuffix);
            sprite.LayerSetShader(index, displaced ? DisplacedShader : Shader);

            var mask = new PrototypeLayerData
            {
                RsiPath = MaskRsi.ToString(),
                State = maskState,
                CopyToShaderParameters = new PrototypeCopyToShaderParameters
                {
                    LayerKey = key,
                    ParameterTexture = "rollMask",
                    ParameterUV = "rollMaskUV",
                },
            };

            // Shader-parameter layers have to sit below the layer they feed, so they're set before it draws.
            var maskKey = key + MaskSuffix;
            _sprite.AddLayer((args.Equipee, sprite), mask, index);
            _sprite.LayerMapSet((args.Equipee, sprite), maskKey, index);
            args.RevealedLayers.Add(maskKey);
        }
    }

    /// <summary>Finds the imported Meridian Rift rolled-down copy of a worn state, if there is one.</summary>
    private bool TryGetDrawnRollDown(RSI rsi, string state, [NotNullWhen(true)] out RSI? drawn)
    {
        drawn = null;
        if (!rsi.Path.TryRelativeTo(SpriteSpecifierSerializer.TextureRoot, out var relative) ||
            !_cache.TryGetResource<RSIResource>(DrawnRoot / relative.Value, out var resource) ||
            !resource.RSI.TryGetState(state, out _))
        {
            return false;
        }

        drawn = resource.RSI;
        return true;
    }
}
