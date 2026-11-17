using Content.Shared._WG.Lathe;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials.OreSilo;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._WG.Lathe.UI;

/// <summary>
/// Opens the fabrication silo window and sends its link, eject, container and reagent requests.
/// </summary>
[UsedImplicitly]
public sealed class FabricationSiloBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private FabricationSiloMenu? _menu;

    protected override void Open()
    {
        base.Open();
        _menu = this.CreateWindow<FabricationSiloMenu>();
        _menu.SetEntity(Owner);
        _menu.OnClientEntryPressed += uid => SendPredictedMessage(new ToggleOreSiloClientMessage(uid));
        _menu.OnPartPressed += uid => SendMessage(new EjectFabricationSiloPartMessage(uid));
        _menu.OnSlotPressed += () =>
            SendMessage(new ItemSlotButtonPressedEvent(FabricationSiloComponent.ContainerSlotId));
        _menu.OnWithdrawPressed += (reagent, amount) =>
            SendMessage(new WithdrawFabricationSiloReagentMessage(reagent, amount));
        _menu.OnDiscardPressed += reagent => SendMessage(new DiscardFabricationSiloReagentMessage(reagent));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OreSiloBuiState silo)
            _menu?.Update(silo);
    }
}
