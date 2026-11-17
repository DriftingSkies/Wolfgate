// using Content.Client._Mono.Shipyard; // WOLFGATE(ShipPreview): unused now
using Content.Client._NF.Shipyard.UI;
// WOLFGATE(ShipPreview): the Wolfgate ship previewer replaces the Mono preview-map flow for this button.
using Content.Client._WG.ShipPreview.UI;
using Content.Shared._WG.Traders; // WOLFGATE(Traders)
using Content.Shared.Containers.ItemSlots;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Events;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._NF.Shipyard.BUI;

public sealed class ShipyardConsoleBoundUserInterface : BoundUserInterface
{
    private ShipyardConsoleMenu? _menu;
    private ShipyardRulesPopup? _rulesWindow;
    // WOLFGATE(ShipPreview): unused, the Wolfgate previewer replaces the Mono preview-map flow
    // [Dependency] private ShipyardPreviewSystem _preview = default!;
    // WOLFGATE(ShipPreview): one shared previewer window per BUI instance, reused across Preview button presses.
    private ShipPreviewWindow? _previewWindow;
    public int Balance { get; private set; }

    public int? ShipSellValue { get; private set; }

    public ShipyardConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _menu = new ShipyardConsoleMenu(this);
        // Disable the NFSD popup for now.
        // var rules = new FormattedMessage();
        // _rulesWindow = new ShipyardRulesPopup(this);
        _menu.OpenCentered();
        // if (ShipyardConsoleUiKey.Security == (ShipyardConsoleUiKey) UiKey)
        // {
        //     rules.AddText(Loc.GetString($"shipyard-rules-default1"));
        //     rules.PushNewline();
        //     rules.AddText(Loc.GetString($"shipyard-rules-default2"));
        //     _rulesWindow.ShipRules.SetMessage(rules);
        //     _rulesWindow.OpenCentered();
        // }
        _menu.OnClose += Close;
        _menu.OnOrderApproved += ApproveOrder;
        _menu.OnSellShip += SellShip;
        _menu.OnUnassignDeed += UnassignDeed;
        _menu.OnRenameShip += RenameShip;
        _menu.TargetIdButton.OnPressed += _ => SendMessage(new ItemSlotButtonPressedEvent("ShipyardConsole-targetId"));
        _menu.OnPreviewShip += PreviewShip;

        // WOLFGATE(Traders) START: an NPC dealer only sells; the card is theirs to hold and the server refuses both buttons.
        if (EntMan.HasComponent<TraderComponent>(Owner))
        {
            _menu.HideSellControls();
            _menu.TargetIdButton.Disabled = true;
        }
        // WOLFGATE END
    }

    private void Populate(List<string> availablePrototypes, List<string> unavailablePrototypes, bool freeListings, bool validId)
    {
        if (_menu == null)
            return;

        _menu.PopulateProducts(availablePrototypes, unavailablePrototypes, freeListings, validId);
        _menu.PopulateCategories(availablePrototypes, unavailablePrototypes);
        _menu.PopulateClasses(availablePrototypes, unavailablePrototypes);
        _menu.PopulateEngines(availablePrototypes, unavailablePrototypes);
        _menu.PopulateClassicFilter(availablePrototypes, unavailablePrototypes); // WOLFGATE(OldVessels): classic ships filter
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ShipyardConsoleInterfaceState cState)
            return;

        Balance = cState.Balance;
        ShipSellValue = cState.ShipSellValue;
        var castState = (ShipyardConsoleInterfaceState) state;
        Populate(castState.ShipyardPrototypes.available, castState.ShipyardPrototypes.unavailable, castState.FreeListings, castState.IsTargetIdPresent);
        _menu?.UpdateState(castState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing) return;

        _menu?.Dispose();
        // WOLFGATE(ShipPreview): close the shared previewer window along with the console menu, so it releases its preview map.
        _previewWindow?.Close();
    }

    private void ApproveOrder(ButtonEventArgs args)
    {
        if (args.Button.Parent?.Parent?.Parent is not VesselRow row || row.Vessel == null) // Mono - another .parent? - this is really fucking stupid
        {
            return;
        }

        var vesselId = row.Vessel.ID;
        SendMessage(new ShipyardConsolePurchaseMessage(vesselId));
    }

    private void SellShip(ButtonEventArgs args)
    {
        //reserved for a sanity check, but im not sure what since we check all the important stuffs on server already
        SendMessage(new ShipyardConsoleSellMessage());
    }

    private void UnassignDeed(ButtonEventArgs args)
    {
        SendMessage(new ShipyardConsoleUnassignDeedMessage());
    }

    private void RenameShip(string newName)
    {
        SendMessage(new ShipyardConsoleRenameMessage(newName));
    }

    private void PreviewShip(ButtonEventArgs args)
    {
        if (args.Button.Parent?.Parent?.Parent is not VesselRow row || row.Vessel == null) // Mono - another .parent? - this is really fucking stupid
        {
            return;
        }

        var vessel = row.Vessel;

        // WOLFGATE(ShipPreview) START: open the client-side ship previewer instead of visiting a server-side preview map.
        // SendMessage(new ShipyardConsolePreviewMessage());
        // _preview.TryPreviewGrid(vessel);
        if (_previewWindow is not { IsOpen: true })
        {
            _previewWindow = ShipPreviewWindow.Open(vessel);
            return;
        }

        _previewWindow.SetVessel(vessel);
        _previewWindow.MoveToFront();
        // WOLFGATE END
    }
}
