using Content.Shared._NF.Bank.Components;

namespace Content.Server._NF.Bank;

public sealed partial class BankSystem
{
    /// <summary>
    /// Brings a mob's bank component back in step after a withdrawal that went through its session.
    /// </summary>
    public void RefreshBalance(EntityUid mobUid)
    {
        if (TryComp<BankAccountComponent>(mobUid, out var bank))
            UpdateBankBalance(mobUid, bank);
    }
}
