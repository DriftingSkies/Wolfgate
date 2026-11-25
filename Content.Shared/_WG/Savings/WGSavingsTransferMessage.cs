using Robust.Shared.Serialization;

namespace Content.Shared._WG.Savings;

/// <summary>
/// Sent from an ATM to move money between the character's bank account and the player's savings.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGSavingsTransferMessage : BoundUserInterfaceMessage
{
    public readonly int Amount;

    /// <summary>True moves bank to savings, false savings to bank.</summary>
    public readonly bool ToSavings;

    public WGSavingsTransferMessage(int amount, bool toSavings)
    {
        Amount = amount;
        ToSavings = toSavings;
    }
}
