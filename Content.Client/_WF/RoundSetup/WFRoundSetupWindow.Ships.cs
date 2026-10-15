using System.Linq;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private string? _selectedVessel;

    private static string Price(int amount) => BankSystemExtensions.ToSpesoString(amount);

    private RichTextLabel VesselSummary(WFRoundSetupState state, WFRoundSetupLobby lobby)
    {
        if (lobby.Vessel == null)
            return Description("ship-unselected");
        var vessel = state.Vessels.FirstOrDefault(choice => choice.Id == lobby.Vessel);
        if (vessel != null)
            return PlainDescription(Text("ship-selected", ("ship", vessel.Name), ("price", Price(vessel.Price))));
        return _prototypes.TryIndex<VesselPrototype>(lobby.Vessel, out var prototype)
            ? PlainDescription(Text("ship-selected", ("ship", prototype.Name), ("price", Price(prototype.Price))))
            : Description("ship-selection-unavailable");
    }

    /// <summary>Previews an explicit ship selection while reserving payment for confirmed launch.</summary>
    private void BuildShipSettings(WFRoundSetupState state, WFRoundSetupLobby lobby, bool host)
    {
        var section = Column(8);
        section.AddChild(Heading("ship"));
        section.AddChild(VesselSummary(state, lobby));
        section.AddChild(Heading("ship-start"));
        if (host && !lobby.Started)
        {
            var location = new WFRoundSetupOptionButton { Name = "RoundSetupShipStart", HorizontalExpand = true };
            location.AddItem(Text("ship-start-random"), (int) WFShipStartLocation.RandomSpace);
            location.AddItem(Text("ship-start-central"), (int) WFShipStartLocation.CentralDock);
            location.SelectId((int) lobby.ShipStart);
            location.OnItemSelected += args => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectShipStart)
            {
                LobbyId = lobby.Id,
                ShipStart = (WFShipStartLocation) args.Id,
            });
            section.AddChild(location);
        }
        section.AddChild(Description(lobby.ShipStart == WFShipStartLocation.CentralDock ? "ship-start-central-help" : "ship-start-random-help"));
        var preview = GetShipPreview();
        if (!host || lobby.Started)
        {
            var vessel = PreviewVessel(lobby.Vessel);
            preview.SetVessel(vessel);
            if (vessel != null)
                section.AddChild(PlainDescription(vessel.Description));
            AddShipPreviewLayout(section, preview);
            return;
        }
        section.AddChild(PlainDescription(state.HostBalance is { } balance
            ? Text("ship-balance", ("balance", Price(balance))) : Text("ship-bank-help")));
        var purchased = state.Vessels.FirstOrDefault(choice => choice.Id == lobby.Vessel);
        if (purchased != null && state.HostBalance is { } funds && funds < purchased.Price)
            section.AddChild(Description("ship-insufficient-help"));
        section.AddChild(Description("ship-purchase-help"));
        if (state.Vessels.Length == 0)
        {
            section.AddChild(Description("ship-empty"));
            preview.SetVessel(PreviewVessel(lobby.Vessel));
            AddShipPreviewLayout(section, preview);
            return;
        }

        var selector = new WFRoundSetupOptionButton
        {
            Name = "RoundSetupShipSelector",
            HorizontalExpand = true,
            Filterable = true,
        };
        selector.AddItem(Text("ship-placeholder"), -1);
        for (var i = 0; i < state.Vessels.Length; i++)
        {
            var vessel = state.Vessels[i];
            selector.AddItem(Text("ship-option", ("ship", vessel.Name), ("price", Price(vessel.Price))), i);
        }
        var selected = Array.FindIndex(state.Vessels, vessel => vessel.Id == (_selectedVessel ?? lobby.Vessel));
        selector.SelectId(selected);
        var choice = selected >= 0 ? state.Vessels[selected] : null;
        var choiceLabel = PlainDescription(choice == null ? Text("ship-select-help") :
            Text("preview-choice", ("ship", choice.Name), ("price", Price(choice.Price))));
        var detail = PlainDescription(choice?.Description ?? string.Empty);
        preview.SetVessel(PreviewVessel(choice?.Id));
        var select = Button("select-ship", () =>
        {
            if (selector.SelectedId < 0)
                return;
            var vessel = state.Vessels[selector.SelectedId];
            _selectedVessel = null;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel) { LobbyId = lobby.Id, Vessel = vessel.Id });
        }, selected < 0);
        select.Name = "RoundSetupSelectShip";
        selector.OnItemSelected += args =>
        {
            selector.SelectId(args.Id);
            var vessel = args.Id < 0 ? null : state.Vessels[args.Id];
            _selectedVessel = vessel?.Id;
            choiceLabel.SetMessage(Robust.Shared.Utility.FormattedMessage.FromUnformatted(vessel == null
                ? Text("ship-select-help") : Text("preview-choice", ("ship", vessel.Name), ("price", Price(vessel.Price)))));
            detail.SetMessage(Robust.Shared.Utility.FormattedMessage.FromUnformatted(vessel?.Description ?? string.Empty));
            preview.SetVessel(PreviewVessel(vessel?.Id));
            select.Disabled = vessel == null;
        };
        section.AddChild(selector);
        section.AddChild(choiceLabel);
        section.AddChild(detail);
        section.AddChild(select);
        AddShipPreviewLayout(section, preview);
    }

    private VesselPrototype? PreviewVessel(string? id) =>
        id != null && _prototypes.TryIndex<VesselPrototype>(id, out var vessel) ? vessel : null;
}
