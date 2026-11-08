using Content.Client._NF.Bank.UI;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Events;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Outposts;

/// <summary>The bank ATM window with its withdrawal half hidden, for the outpost deposit terminal.</summary>
[UsedImplicitly]
public sealed class WFDepositTerminalBoundUserInterface : BoundUserInterface
{
    private BankATMMenu? _menu;

    public WFDepositTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<BankATMMenu>();
        _menu.Title = Loc.GetString("wf-outpost-deposit-title");
        foreach (var name in new[] { "WithdrawEdit", "WithdrawButton" })
        {
            var control = _menu.FindControl<Control>(name);
            control.Visible = false;
            if (control.Parent is PanelContainer panel)
                panel.Visible = false;
        }

        _menu.DepositRequest += () => SendMessage(new BankDepositMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not BankATMMenuInterfaceState bank || _menu == null)
            return;

        _menu.SetEnabled(bank.Enabled);
        _menu.SetBalance(bank.Balance, bank.Savings);
        _menu.SetDeposit(bank.Deposit);
    }
}
