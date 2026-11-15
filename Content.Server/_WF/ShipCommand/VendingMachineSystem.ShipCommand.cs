using Content.Server._WF.ShipCommand;
using Content.Shared._WF.ShipCommand;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;

namespace Content.Server.VendingMachines;

public sealed partial class VendingMachineSystem
{
    [Dependency] private WFShipServiceSystem _wfShipServices = default!;

    /// <summary>The usual price and the rule of the vend just quoted, for its log line; the server vends one at a time.</summary>
    private int _wfShipBasePrice;

    private string? _wfShipRule;

    /// <summary>Vends sold below their usual price, by machine, until the item comes out: the usual price and what was paid.</summary>
    private readonly Dictionary<EntityUid, (int Base, int Paid)> _wfShipBargains = new();

    /// <summary>True, with a popup and the machine's deny, when a vending machine aboard a commanded ship turns the customer away.</summary>
    private bool WfShipCommandRefuses(EntityUid vendor, EntityUid customer, VendingMachineComponent component)
    {
        if (!_wfShipServices.Refuses(vendor, customer, out var reason))
            return false;

        _popupSystem.PopupEntity(reason, vendor, customer);
        Deny(vendor, component);
        return true;
    }

    /// <summary>What a customer pays at a vending machine aboard a commanded ship; the price unchanged anywhere else.</summary>
    private int WfShipCommandPrice(EntityUid vendor, EntityUid customer, int price)
    {
        _wfShipBasePrice = price;
        return _wfShipServices.Quote(vendor, customer, WFShipServiceKind.Vending, price, out _wfShipRule);
    }

    /// <summary>
    /// Logs a vend aboard a commanded ship as service use, and holds on to a vend sold below its usual price until the
    /// item comes out.
    /// </summary>
    private void WfShipCommandRecord(EntityUid vendor, EntityUid customer, EntityPrototype proto, int charged)
    {
        _wfShipServices.Record(vendor, customer, WFShipServiceKind.Vending, proto.Name, _wfShipBasePrice, charged, _wfShipRule);

        // A free machine (an expedition's) quotes nothing, so its vends would resell at full value: an endless money
        // tap with restock boxes. They are bargains against what the item is worth.
        var worth = charged <= 0 && _wfShipBasePrice <= 0 ? (int) _pricing.GetEstimatedPrice(proto) : _wfShipBasePrice;
        if (charged < worth)
            _wfShipBargains[vendor] = (worth, charged);
        else
            _wfShipBargains.Remove(vendor);
    }

    /// <summary>Marks a vended item sold below its usual price, so it resells for no more than was paid.</summary>
    private void WfShipCommandMarkVended(EntityUid vendor, EntityUid item)
    {
        if (_wfShipBargains.Remove(vendor, out var bargain))
            _wfShipServices.MarkBargainGoods(item, bargain.Base, bargain.Paid);
    }
}
