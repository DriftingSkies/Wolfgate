using Content.Shared._WG.NpcCrew;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipCommand;

/// <summary>The ship command console's window.</summary>
[Serializable, NetSerializable]
public enum WGShipConsoleUiKey : byte
{
    Key,
}

/// <summary>The owner's on-off switches the console shows.</summary>
[Serializable, NetSerializable]
public enum WGShipConsoleSwitch : byte
{
    AdminsInZones,
    AdminsAtConsole,
    AdminsNeverTargeted,
    AdminsServedFree,
    EventStaffAreAdmins,
    OwnerServedFree,
    DocksWelcome,
    AutoRestaff,
}

/// <summary>Where the crew's answer to someone who may not be there is set: a zone mode, or the barred list.</summary>
[Serializable, NetSerializable]
public enum WGShipConsoleResponseSlot : byte
{
    Staff,
    Locked,
    Sealed,
    Barred,
}

/// <summary>What the Presets tab does with one of the viewer's presets.</summary>
[Serializable, NetSerializable]
public enum WGShipConsolePresetAction : byte
{
    Save,
    Load,
    Delete,
}

/// <summary>How a roster post stands.</summary>
[Serializable, NetSerializable]
public enum WGShipConsoleSlotState : byte
{
    Staffed,
    Empty,
    StoodDown,
    Player,
}

/// <summary>A zone as the console shows it, with how many doors and lockers it holds.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsoleZone(string Id, string Name, WGShipZoneMode Mode, int Doors, int Lockers);

/// <summary>Someone on a command list, and whether they match by card, by player, or both.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsolePerson(string Name, bool Card, bool Player);

/// <summary>Someone aboard who could be put on a list, with where they stand now.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsoleAboard(NetEntity Entity, string Name, WGShipAuthority Authority);

/// <summary>A price rule as the console shows it; <see cref="Who"/> names its person or company.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsoleRule(string Name, WGShipPriceAudience Audience, string Who, WGShipServiceKind Services, float Multiplier, bool Enabled);

/// <summary>A roster post: what stands there, whether it is a counter, how it stands and who holds it.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsoleSlot(string Id, string What, bool Counter, WGShipConsoleSlotState State, string Who);

/// <summary>A player holding one of the ship's jobs.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsoleHolder(NetEntity Entity, string Name);

/// <summary>A player job: its open slots and who holds it.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsoleJob(string Id, string Name, int Slots, List<WGShipConsoleHolder> Holders);

/// <summary>One of the viewer's presets for this vessel, and when it was saved.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsolePreset(string Name, DateTime Saved);

/// <summary>A recovery pad aboard and who may use it.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsolePad(NetEntity Entity, string Name, WGShipAuthority Access);

/// <summary>One service log entry: who, what, the usual price, what they paid and the rule that set it.</summary>
[Serializable, NetSerializable]
public sealed record WGShipConsoleLogEntry(TimeSpan Time, string Customer, WGShipServiceKind Kind, string Service, string What, int BasePrice, int Charged, string? Rule);

/// <summary>
/// The ship command console's state for one viewer. Officers see it all and change nothing; whoever configures the ship
/// changes it; the log is for the owner and admins. Anyone else sees only that they are not cleared.
/// </summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleState
{
    /// <summary>The console stands on a commanded ship.</summary>
    public bool Commanded;

    /// <summary>The ship's name.</summary>
    public string Ship = string.Empty;

    /// <summary>Where the viewer stands with the ship.</summary>
    public WGShipAuthority Authority = WGShipAuthority.Guest;

    /// <summary>The viewer sees the tabs: they operate the ship.</summary>
    public bool CanView;

    /// <summary>The viewer may change the ship's settings.</summary>
    public bool CanConfigure;

    /// <summary>The viewer sees the service log: the owner or an admin.</summary>
    public bool CanSeeLog;

    /// <summary>The ship's zones.</summary>
    public List<WGShipConsoleZone> Zones = new();

    /// <summary>The crew's answer to someone who may not be where they are, for each case.</summary>
    public Dictionary<WGShipConsoleResponseSlot, WGCrewSecurityResponse> Responses = new();

    /// <summary>Where each of the owner's switches stands.</summary>
    public Dictionary<WGShipConsoleSwitch, bool> Switches = new();

    /// <summary>The officers list.</summary>
    public List<WGShipConsolePerson> Officers = new();

    /// <summary>The staff list.</summary>
    public List<WGShipConsolePerson> Staff = new();

    /// <summary>The barred list.</summary>
    public List<WGShipConsolePerson> Barred = new();

    /// <summary>Everyone aboard who could be put on a list.</summary>
    public List<WGShipConsoleAboard> Aboard = new();

    /// <summary>The ship's price rules.</summary>
    public List<WGShipConsoleRule> Rules = new();

    /// <summary>The ship has a crew roster.</summary>
    public bool HasRoster;

    /// <summary>The roster's posts, by id.</summary>
    public List<WGShipConsoleSlot> Roster = new();

    /// <summary>How long a post stays empty before automatic restaffing fills it, in seconds.</summary>
    public int RestaffDelay;

    /// <summary>The most automatic restaffs in an hour.</summary>
    public int RestaffsPerHour;

    /// <summary>Where the marines stand when nothing is happening.</summary>
    public WGShipMarineOrders MarineOrders;

    /// <summary>When the gunners open fire without being given a target.</summary>
    public WGGunnerPolicy GunnerPolicy;

    /// <summary>What the pilot does when another ship attacks.</summary>
    public WGShipPilotStance PilotStance;

    /// <summary>The radio officer's callsign; empty for the ship's own name.</summary>
    public string Callsign = string.Empty;

    /// <summary>The ship has player jobs.</summary>
    public bool HasJobs;

    /// <summary>The ship's player jobs, by id.</summary>
    public List<WGShipConsoleJob> Jobs = new();

    /// <summary>The vessel the viewer's presets are for; null when the ship has none.</summary>
    public string? Vessel;

    /// <summary>The viewer's presets for the vessel.</summary>
    public List<WGShipConsolePreset> Presets = new();

    /// <summary>The recovery pads aboard.</summary>
    public List<WGShipConsolePad> Pads = new();

    /// <summary>The newest service log entries, newest first.</summary>
    public List<WGShipConsoleLogEntry> Log = new();
}

/// <summary>The console's state, sent to one viewer: what they see depends on who they are.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleStateMessage(WGShipConsoleState state) : BoundUserInterfaceMessage
{
    public readonly WGShipConsoleState State = state;
}

/// <summary>Sent by the window once it is open, and by its refresh button.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleRefreshMessage : BoundUserInterfaceMessage;

/// <summary>Switch one zone, or every zone when <see cref="Zone"/> is null.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleZoneMessage(string? zone, WGShipZoneMode mode) : BoundUserInterfaceMessage
{
    public readonly string? Zone = zone;
    public readonly WGShipZoneMode Mode = mode;
}

/// <summary>Set the crew's answer for one case.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleResponseMessage(WGShipConsoleResponseSlot slot, WGCrewSecurityResponse response) : BoundUserInterfaceMessage
{
    public readonly WGShipConsoleResponseSlot Slot = slot;
    public readonly WGCrewSecurityResponse Response = response;
}

/// <summary>Turn one of the owner's switches on or off.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleSwitchMessage(WGShipConsoleSwitch which, bool value) : BoundUserInterfaceMessage
{
    public readonly WGShipConsoleSwitch Which = which;
    public readonly bool Value = value;
}

/// <summary>Put someone aboard on a command list, by their card and their player.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleAddPersonMessage(NetEntity person, WGShipCommandList list) : BoundUserInterfaceMessage
{
    public readonly NetEntity Person = person;
    public readonly WGShipCommandList List = list;
}

/// <summary>Change the crew's standing orders; whatever is null stays as it is.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleOrdersMessage(WGShipMarineOrders? marines, WGGunnerPolicy? gunners, WGShipPilotStance? pilot, string? callsign) : BoundUserInterfaceMessage
{
    public readonly WGShipMarineOrders? Marines = marines;
    public readonly WGGunnerPolicy? Gunners = gunners;
    public readonly WGShipPilotStance? Pilot = pilot;
    public readonly string? Callsign = callsign;
}

/// <summary>Take the person at <see cref="Index"/> off a command list, if that is still who is named there.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleRemovePersonMessage(WGShipCommandList list, int index, string name) : BoundUserInterfaceMessage
{
    public readonly WGShipCommandList List = list;
    public readonly int Index = index;
    public readonly string Name = name;
}

/// <summary>Add a price rule, or replace the one with the same name.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleSetRuleMessage(string name, WGShipPriceAudience audience, NetEntity? person, string? company,
    WGShipServiceKind services, float multiplier) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
    public readonly WGShipPriceAudience Audience = audience;
    public readonly NetEntity? Person = person;
    public readonly string? Company = company;
    public readonly WGShipServiceKind Services = services;
    public readonly float Multiplier = multiplier;
}

/// <summary>Switch a price rule on or off.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleToggleRuleMessage(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

/// <summary>Remove a price rule.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleRemoveRuleMessage(string name) : BoundUserInterfaceMessage
{
    public readonly string Name = name;
}

/// <summary>Staff one roster post, or every empty one when <see cref="Slot"/> is null.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleStaffMessage(string? slot) : BoundUserInterfaceMessage
{
    public readonly string? Slot = slot;
}

/// <summary>Stand a roster post down, or up again.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleStandDownMessage(string slot, bool down) : BoundUserInterfaceMessage
{
    public readonly string Slot = slot;
    public readonly bool Down = down;
}

/// <summary>Automatic restaffing's delay, in seconds, and its hourly cap.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleRestaffMessage(int delay, int perHour) : BoundUserInterfaceMessage
{
    public readonly int Delay = delay;
    public readonly int PerHour = perHour;
}

/// <summary>Set how many slots a player job has open.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleJobSlotsMessage(string job, int slots) : BoundUserInterfaceMessage
{
    public readonly string Job = job;
    public readonly int Slots = slots;
}

/// <summary>Release a player from their job, handing the post back to the roster.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsoleReleaseJobMessage(NetEntity holder) : BoundUserInterfaceMessage
{
    public readonly NetEntity Holder = holder;
}

/// <summary>Save, load or delete one of the viewer's presets by name.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsolePresetMessage(WGShipConsolePresetAction action, string name) : BoundUserInterfaceMessage
{
    public readonly WGShipConsolePresetAction Action = action;
    public readonly string Name = name;
}

/// <summary>Change who may use one of the recovery pads aboard.</summary>
[Serializable, NetSerializable]
public sealed class WGShipConsolePadAccessMessage(NetEntity pad, WGShipAuthority access) : BoundUserInterfaceMessage
{
    public readonly NetEntity Pad = pad;
    public readonly WGShipAuthority Access = access;
}
