using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipAccess;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipCommand;

/// <summary>
/// Who commands a large admin vessel besides its owner: officers, who operate it but change no settings, the staff
/// list and the barred list, and which of the owner's powers admins share. Lives on the grid. The owner is ShipAccess's
/// (the deed, or the players an admin tool registered the ship to). The lists are round state, as card keys point at
/// this round's stations; presets carry people between rounds by player. The lists and price rules stay on the server
/// (they name players and cards); clients get the switches.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGShipCommandComponent : Component
{
    /// <summary>People who operate the ship (helm, gunnery, drydock jobs, hails) but change none of its settings.</summary>
    [ViewVariables]
    public List<WGShipCommandPerson> Officers = new();

    /// <summary>People admitted to Staff zones and priced as staff.</summary>
    [ViewVariables]
    public List<WGShipCommandPerson> Staff = new();

    /// <summary>People the crew treat as boarders everywhere and the counters refuse.</summary>
    [ViewVariables]
    public List<WGShipCommandPerson> Barred = new();

    /// <summary>Admins open Staff and Locked zone doors with the ID card they wear.</summary>
    [DataField, AutoNetworkedField]
    public bool AdminsInZones = true;

    /// <summary>Admins change the ship's settings at its command console as the owner does.</summary>
    [DataField, AutoNetworkedField]
    public bool AdminsAtConsole = true;

    /// <summary>The ship's crew never treat an admin as a boarder or a target.</summary>
    [DataField, AutoNetworkedField]
    public bool AdminsNeverTargeted = true;

    /// <summary>Admins are served free wherever the ship's pricing applies.</summary>
    [DataField, AutoNetworkedField]
    public bool AdminsServedFree = true;

    /// <summary>Event staff (the Event admin flag) count as admins for every power above.</summary>
    [DataField, AutoNetworkedField]
    public bool EventStaffAreAdmins = true;

    /// <summary>How the crew answer someone in a Staff zone who may not be there.</summary>
    [DataField, AutoNetworkedField]
    public WGCrewSecurityResponse StaffZoneResponse = WGCrewSecurityResponse.Warn;

    /// <summary>How the crew answer someone in a Locked zone who may not be there.</summary>
    [DataField, AutoNetworkedField]
    public WGCrewSecurityResponse LockedZoneResponse = WGCrewSecurityResponse.Warn;

    /// <summary>How the crew answer someone inside a Sealed zone, who got past bolted doors.</summary>
    [DataField, AutoNetworkedField]
    public WGCrewSecurityResponse SealedZoneResponse = WGCrewSecurityResponse.Hostile;

    /// <summary>How the crew answer someone on the barred list, wherever they are aboard.</summary>
    [DataField, AutoNetworkedField]
    public WGCrewSecurityResponse BarredResponse = WGCrewSecurityResponse.Warn;

    /// <summary>The crew welcome any ship that docks, as a station's would.</summary>
    [DataField, AutoNetworkedField]
    public bool DocksWelcome = true;

    /// <summary>The owner is served free wherever the ship's pricing applies.</summary>
    [DataField, AutoNetworkedField]
    public bool OwnerServedFree = true;

    /// <summary>The ship's price rules; see <see cref="WGShipPriceRule"/>.</summary>
    [DataField]
    public List<WGShipPriceRule> PriceRules = new();

    /// <summary>Only the staff list and up open maintenance panels or use an emag aboard, so nobody else hacks its doors
    /// or machines.</summary>
    [DataField, AutoNetworkedField]
    public bool StaffOnlyWires;
}

/// <summary>
/// One person on a ship's command lists: their ID card's record key, their player, or both. Either one matches, so
/// a player keeps their place on a new card, and a card keeps its place in another's hands.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGShipCommandPerson
{
    /// <summary>The record key of the card they were added with.</summary>
    public WGShipAccessKey? Key;

    /// <summary>The player, when known; presets add people by player alone.</summary>
    public NetUserId? User;

    /// <summary>The name shown on the console.</summary>
    public string Name = string.Empty;
}

/// <summary>The command lists a person can be put on.</summary>
[Serializable, NetSerializable]
public enum WGShipCommandList : byte
{
    Officers,
    Staff,
    Barred,
}

/// <summary>Where someone stands with a commanded ship, lowest first.</summary>
[Serializable, NetSerializable]
public enum WGShipAuthority : byte
{
    /// <summary>On the barred list: a boarder everywhere, with no discounts or treatment.</summary>
    Barred,

    /// <summary>Everyone else aboard: Public zones and public prices.</summary>
    Guest,

    /// <summary>On the staff list: Staff zones and staff prices.</summary>
    Staff,

    /// <summary>On the officers list: Staff and Locked zones, and operates the ship without changing its settings.</summary>
    Officer,

    /// <summary>An admin, or event staff when they count: the owner's powers, each switchable by the owner.</summary>
    Admin,

    /// <summary>The deed holder, or a player the ship is registered to.</summary>
    Owner,
}
