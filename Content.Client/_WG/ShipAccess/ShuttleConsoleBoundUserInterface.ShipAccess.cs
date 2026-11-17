using Content.Shared._WG.ShipAccess;

namespace Content.Client.Shuttles.BUI;

public sealed partial class ShuttleConsoleBoundUserInterface
{
    private void WgAccessOpen()
    {
        if (_window == null)
            return;

        _window.ShipAccessModeRequested += mode => SendMessage(new WGShipAccessSetModeMessage(mode));
        _window.ShipAccessLockedRequested += locked => SendMessage(new WGShipAccessSetLockedMessage(locked));
        _window.ShipAccessAddRequested += target => SendMessage(new WGShipAccessAddPlayerMessage(target));
        _window.ShipAccessRemoveRequested += key => SendMessage(new WGShipAccessRemoveMessage(key));
        _window.ShipAccessBuilderRequested += (key, builder) => SendMessage(new WGShipAccessSetBuilderMessage(key, builder));
        _window.ShipAccessDoorRuleRequested += (door, rule) => SendMessage(new WGShipAccessSetDoorRuleMessage(door, rule));
        _window.ShipAccessAllDoorRulesRequested += rule => SendMessage(new WGShipAccessSetAllDoorRulesMessage(rule));
        _window.ShipAccessDoorPlayerRequested +=(door, key, listed) => SendMessage(new WGShipAccessSetDoorPlayerMessage(door, key, listed));
        _window.ShipAccessCodesRequested += () => SendMessage(new WGShipAccessRequestCodesMessage());
        _window.ShipAccessShipCodeRequested += code => SendMessage(new WGShipAccessSetShipCodeMessage(code));
        _window.ShipAccessDoorCodeRequested += (door, code) => SendMessage(new WGShipAccessSetDoorCodeMessage(door, code));
    }
}
