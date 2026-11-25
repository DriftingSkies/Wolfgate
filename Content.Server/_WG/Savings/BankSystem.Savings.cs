using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;

namespace Content.Server._NF.Bank;

public sealed partial class BankSystem
{
    /// <summary>
    /// Sends an ATM window the player's bank balance, savings and the cash in the slot.
    /// </summary>
    public void UpdateAtmUi(EntityUid atm, BankATMComponent component, EntityUid player, Enum uiKey)
    {
        GetInsertedCashAmount(component, out var deposit);
        var state = new BankATMMenuInterfaceState(0, 0, false, deposit);

        if (TryComp<BankAccountComponent>(player, out var bank))
        {
            state.Balance = bank.Balance;
            state.Savings = GetEntSavings(player);
            state.Enabled = true;
        }

        _uiSystem.SetUiState(atm, uiKey, state);
    }
}
