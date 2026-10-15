using System.Linq;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
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

    /// <summary>Keeps vessel selection, departure and the preview together in the available page.</summary>
    private void BuildShipSettings(WFRoundSetupState state, WFRoundSetupLobby lobby, bool host)
    {
        var section = Column(8);
        var preview = GetShipPreview();
        section.AddChild(Heading("ship"));
        if (!host || lobby.Started)
        {
            var vessel = PreviewVessel(lobby.Vessel);
            var summary = VesselSummary(state, lobby);
            summary.ToolTip = vessel?.Description;
            section.AddChild(summary);
            section.AddChild(Heading("ship-start"));
            section.AddChild(Description(lobby.ShipStart == WFShipStartLocation.CentralDock ? "ship-start-central" : "ship-start-random"));
            preview.SetVessel(vessel);
            preview.ToolTip = vessel?.Description;
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
        selector.DisableItem(0, Text("ship-unselected"));
        for (var i = 0; i < state.Vessels.Length; i++)
        {
            var vessel = state.Vessels[i];
            selector.AddItem(Text("ship-option", ("ship", vessel.Name), ("price", Price(vessel.Price))), i);
        }
        var selected = Array.FindIndex(state.Vessels, vessel => vessel.Id == lobby.Vessel);
        selector.SelectId(selected);
        var choice = selected >= 0 ? state.Vessels[selected] : null;
        selector.ToolTip = choice?.Description;
        preview.SetVessel(PreviewVessel(choice?.Id));
        preview.ToolTip = choice?.Description;
        selector.OnItemSelected += args =>
        {
            selector.SelectId(args.Id);
            var vessel = args.Id < 0 ? null : state.Vessels[args.Id];
            selector.ToolTip = vessel?.Description;
            preview.ToolTip = vessel?.Description;
            preview.SetVessel(PreviewVessel(vessel?.Id));
            if (vessel != null)
                SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectVessel) { LobbyId = lobby.Id, Vessel = vessel.Id });
        };
        section.AddChild(selector);
        section.AddChild(PlainDescription(state.HostBalance is { } balance
            ? Text("ship-balance", ("balance", Price(balance))) : Text("ship-bank-help")));
        if (state.Vessels.Length == 0)
            section.AddChild(Description("ship-empty"));
        section.AddChild(Heading("ship-start"));
        var location = new WFRoundSetupOptionButton { Name = "RoundSetupShipStart", HorizontalExpand = true };
        location.AddItem(Text("ship-start-random"), (int) WFShipStartLocation.RandomSpace);
        location.AddItem(Text("ship-start-central"), (int) WFShipStartLocation.CentralDock);
        location.SelectId((int) lobby.ShipStart);
        location.ToolTip = Text(lobby.ShipStart == WFShipStartLocation.CentralDock ? "ship-start-central-help" : "ship-start-random-help");
        location.OnItemSelected += args => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SelectShipStart)
        {
            LobbyId = lobby.Id,
            ShipStart = (WFShipStartLocation) args.Id,
        });
        section.AddChild(location);

        AddShipPreviewLayout(section, preview);
    }

    private VesselPrototype? PreviewVessel(string? id) =>
        id != null && _prototypes.TryIndex<VesselPrototype>(id, out var vessel) ? vessel : null;
}
