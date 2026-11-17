using Content.Shared._WG.ShipCommand;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// The round's record of service use aboard a commanded ship, newest last and capped; the admin log keeps every
/// line for good. Server only, round state.
/// </summary>
[RegisterComponent]
public sealed partial class WGShipServiceLedgerComponent : Component
{
    [ViewVariables]
    public List<WGShipServiceEntry> Entries = new();
}

/// <summary>One use of a service: when (round time), who, which service and what, the usual price, what was charged and the rule.</summary>
public readonly record struct WGShipServiceEntry(
    TimeSpan Time,
    string Customer,
    WGShipServiceKind Kind,
    string Service,
    string What,
    int BasePrice,
    int Charged,
    string? Rule);
