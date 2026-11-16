using Content.Client.Clothing;
using Content.Client.Toggleable;
using Content.Shared._WF.Modsuits.Modules;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Robust.Client.GameObjects;
using Robust.Shared.Serialization.Manager;

namespace Content.Client._WF.Modsuits;

public sealed partial class ModHatVisualsSystem : EntitySystem
{
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;

    [SubscribeLocalEvent(after: [typeof(ClientClothingSystem), typeof(ModSuitVisualizerSystem), typeof(ToggleableVisualsSystem)])]
    private void OnGetEquipmentVisuals(Entity<ModHatHolderComponent> ent, ref GetEquipmentVisualsEvent args)
    {
        if (_itemSlots.GetItemOrNull(ent, ent.Comp.SlotId) is not { } hat)
            return;

        var hatEv = new GetEquipmentVisualsEvent(args.Equipee, args.Slot);
        RaiseLocalEvent(hat, hatEv);

        var rsiPath = CompOrNull<ClothingComponent>(hat)?.RsiPath
            ?? CompOrNull<SpriteComponent>(hat)?.BaseRSI?.Path.ToString();

        foreach (var (key, layer) in hatEv.Layers)
        {
            var data = layer;
            if (data.RsiPath == null && data.TexturePath == null && rsiPath != null)
            {
                data = _serialization.CreateCopy(layer, notNullableOverride: true);
                data.RsiPath = rsiPath;
            }

            args.Layers.Add(($"mod-hat-{key}", data));
        }
    }
}
