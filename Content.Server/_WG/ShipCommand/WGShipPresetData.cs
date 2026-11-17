using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipCommand;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's settings as a preset stores them, as JSON in the database: zone modes, the command lists by
/// player, the admin powers and crew responses, price rules, the roster's restaffing and stood-down posts, the
/// callsign, the crew's standing orders and the open job slots. People are kept by player, since cards belong to one round.
/// </summary>
public sealed class WGShipPresetData
{
    /// <summary>The format's version, for reading older presets.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Each zone's mode, by zone id.</summary>
    public Dictionary<string, WGShipZoneMode> Zones { get; set; } = new();

    /// <summary>The officers list, by player.</summary>
    public List<WGShipPresetPerson> Officers { get; set; } = new();

    /// <summary>The staff list, by player.</summary>
    public List<WGShipPresetPerson> Staff { get; set; } = new();

    /// <summary>The barred list, by player.</summary>
    public List<WGShipPresetPerson> Barred { get; set; } = new();

    /// <summary>See <see cref="WGShipCommandComponent.AdminsInZones"/>.</summary>
    public bool AdminsInZones { get; set; } = true;

    /// <summary>See <see cref="WGShipCommandComponent.AdminsAtConsole"/>.</summary>
    public bool AdminsAtConsole { get; set; } = true;

    /// <summary>See <see cref="WGShipCommandComponent.AdminsNeverTargeted"/>.</summary>
    public bool AdminsNeverTargeted { get; set; } = true;

    /// <summary>See <see cref="WGShipCommandComponent.AdminsServedFree"/>.</summary>
    public bool AdminsServedFree { get; set; } = true;

    /// <summary>See <see cref="WGShipCommandComponent.EventStaffAreAdmins"/>.</summary>
    public bool EventStaffAreAdmins { get; set; } = true;

    /// <summary>See <see cref="WGShipCommandComponent.OwnerServedFree"/>.</summary>
    public bool OwnerServedFree { get; set; } = true;

    /// <summary>See <see cref="WGShipCommandComponent.DocksWelcome"/>.</summary>
    public bool DocksWelcome { get; set; } = true;

    /// <summary>The crew's answer to someone in a Staff zone who may not be there.</summary>
    public WGCrewSecurityResponse StaffZoneResponse { get; set; } = WGCrewSecurityResponse.Warn;

    /// <summary>The crew's answer to someone in a Locked zone who may not be there.</summary>
    public WGCrewSecurityResponse LockedZoneResponse { get; set; } = WGCrewSecurityResponse.Warn;

    /// <summary>The crew's answer to someone inside a Sealed zone.</summary>
    public WGCrewSecurityResponse SealedZoneResponse { get; set; } = WGCrewSecurityResponse.Hostile;

    /// <summary>The crew's answer to someone on the barred list.</summary>
    public WGCrewSecurityResponse BarredResponse { get; set; } = WGCrewSecurityResponse.Warn;

    /// <summary>The price rules; a rule for one person keeps them by player.</summary>
    public List<WGShipPresetPriceRule> PriceRules { get; set; } = new();

    /// <summary>Lost posts are restaffed on their own.</summary>
    public bool AutoRestaff { get; set; }

    /// <summary>How long a post stays empty before automatic restaffing fills it, in seconds.</summary>
    public int RestaffDelaySeconds { get; set; } = 120;

    /// <summary>The most automatic restaffs in an hour.</summary>
    public int RestaffsPerHour { get; set; } = 6;

    /// <summary>The ids of the posts stood down.</summary>
    public List<string> StoodDown { get; set; } = new();

    /// <summary>The radio officer's callsign; empty for the ship's own name.</summary>
    public string Callsign { get; set; } = string.Empty;

    /// <summary>Where the marines stand when nothing is happening.</summary>
    public WGShipMarineOrders MarineOrders { get; set; } = WGShipMarineOrders.Patrol;

    /// <summary>When the gunners open fire without being given a target.</summary>
    public WGGunnerPolicy GunnerPolicy { get; set; } = WGGunnerPolicy.Hostiles;

    /// <summary>What the pilot does when another ship attacks.</summary>
    public WGShipPilotStance PilotStance { get; set; } = WGShipPilotStance.Evade;

    /// <summary>Open slots per job id; a job left out is closed.</summary>
    public Dictionary<string, int> Jobs { get; set; } = new();

    /// <summary>Who may use the ship's recovery pads; null when it has none.</summary>
    public WGShipAuthority? PadAccess { get; set; }
}

/// <summary>A person on a command list, by player.</summary>
public sealed class WGShipPresetPerson
{
    /// <summary>The player's user id.</summary>
    public Guid User { get; set; }

    /// <summary>The name shown on the console.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>A price rule as a preset stores it; a rule for one person keeps them by player.</summary>
public sealed class WGShipPresetPriceRule
{
    /// <summary>See <see cref="WGShipPriceRule.Name"/>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>See <see cref="WGShipPriceRule.Audience"/>.</summary>
    public WGShipPriceAudience Audience { get; set; }

    /// <summary>See <see cref="WGShipPriceRule.Company"/>.</summary>
    public string? Company { get; set; }

    /// <summary>See <see cref="WGShipPriceRule.Services"/>.</summary>
    public WGShipServiceKind Services { get; set; } = WGShipServiceKind.All;

    /// <summary>See <see cref="WGShipPriceRule.Multiplier"/>.</summary>
    public float Multiplier { get; set; }

    /// <summary>See <see cref="WGShipPriceRule.Enabled"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The player a rule for one person is for.</summary>
    public Guid? PersonUser { get; set; }

    /// <summary>That person's name, shown on the console.</summary>
    public string? PersonName { get; set; }
}
