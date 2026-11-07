using Robust.Shared.Serialization;
// WOLFGATE(Gags): carry chosen clothing colours with a purchase
using Content.Shared._WF.Gags;

namespace Content.Shared.VendingMachines
{
    [Serializable, NetSerializable]
    public sealed class VendingMachineEjectMessage : BoundUserInterfaceMessage
    {
        public readonly InventoryType Type;
        public readonly string ID;
        // WOLFGATE(Gags) START: recolour an eligible item before it is dispensed
        public readonly List<Color>? Colors;
        public VendingMachineEjectMessage(InventoryType type, string id, List<Color>? colors = null)
        {
            Type = type;
            ID = id;
            Colors = WFGagsColors.Sanitize(colors);
        }
        // WOLFGATE END
    }

    [Serializable, NetSerializable]
    public enum VendingMachineUiKey
    {
        Key,
    }
}
