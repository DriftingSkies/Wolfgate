using System.Numerics;
using Content.Shared._WF.ShipAccess;
using Content.Shared.Actions;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.ShipAdmin;

/// <summary>Console command names for Shipmaster, the admin ship panel.</summary>
public static class WFShipAdminCommands
{
    public const string Panel = "shipmaster";
}

/// <summary>Raised by the action staff get while they are over a ship: opens Shipmaster on it.</summary>
public sealed partial class WFShipAdminOpenActionEvent : InstantActionEvent;

/// <summary>Which grids the panel lists.</summary>
[Serializable, NetSerializable]
public enum WFShipAdminFilter : byte
{
    /// <summary>Grids with a deed, an owner, ship access or a vessel design.</summary>
    PlayerShips,
    AllGrids,
}

/// <summary>Whether a ship has an owner and whether any of them is connected.</summary>
[Serializable, NetSerializable]
public enum WFShipAdminOwnerState : byte
{
    None,
    Online,
    Offline,
}

/// <summary>What a list row shows as small tags.</summary>
[Flags, Serializable, NetSerializable]
public enum WFShipAdminRowFlags : ushort
{
    None = 0,
    Locked = 1 << 0,
    Docked = 1 << 1,
    InFtl = 1 << 2,
    Snapshot = 1 << 3,
    CrewLinked = 1 << 4,
    Company = 1 << 5,
    Hangar = 1 << 6,
    Fixing = 1 << 7,
}

/// <summary>One ship in the panel's list.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminRow
{
    public NetEntity Grid;
    public string Name = string.Empty;
    public string Design = string.Empty;
    public string Owner = string.Empty;
    public WFShipAdminOwnerState OwnerState;
    public string Map = string.Empty;
    public int Occupants;
    public WFShipAdminRowFlags Flags;

    /// <summary>Where the ship was when the list was built, for the client to sort by nearness and point at it.</summary>
    public MapId MapId;
    public Vector2 Position;
}

/// <summary>How someone owns a ship.</summary>
[Serializable, NetSerializable]
public enum WFShipAdminOwnerKind : byte
{
    /// <summary>An ID card holding the deed.</summary>
    DeedCard,

    /// <summary>A voucher or other item holding the deed.</summary>
    Voucher,

    /// <summary>A player's account, whatever body they play.</summary>
    Account,
}

/// <summary>One owner of the selected ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminOwner
{
    public WFShipAdminOwnerKind Kind;
    public string Name = string.Empty;

    /// <summary>The account, when it is known: always for <see cref="WFShipAdminOwnerKind.Account"/>, for a card when it was issued to a player.</summary>
    public NetUserId? User;
    public string Username = string.Empty;
    public bool Online;

    /// <summary>The card or voucher, for the deed kinds.</summary>
    public NetEntity? Card;
}

/// <summary>Someone aboard the selected ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminOccupant
{
    public string Name = string.Empty;
    public string Username = string.Empty;
}

/// <summary>Everything the panel shows about the selected ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminDetail
{
    public NetEntity Grid;
    public string Name = string.Empty;
    public string Design = string.Empty;
    public string Map = string.Empty;
    public MapId MapId;
    public Vector2 Position;
    public float Mass;
    public float Speed;

    /// <summary>Whether the ship travels along when a ship it is docked to jumps.</summary>
    public bool FtlLock;

    /// <summary>Where the ship is in a jump, or empty when it is not in one.</summary>
    public string Ftl = string.Empty;
    public WFShipAdminRowFlags Flags;
    public List<string> DockedTo = new();

    public List<WFShipAdminOwner> Owners = new();

    /// <summary>The owner name on the deed and shuttle record.</summary>
    public string RecordOwner = string.Empty;

    /// <summary>The account the shipyard registered the ship to, and whether it is connected.</summary>
    public string Registered = string.Empty;
    public bool RegisteredOnline;

    public bool HasAccess;
    public WFShipAccessMode Mode;
    public bool Locked;
    public bool CanLock;
    public List<WFShipAccessEntry> AllowList = new();
    public string Crew = string.Empty;
    public string Company = string.Empty;
    public string Faction = string.Empty;

    public List<WFShipAdminOccupant> Occupants = new();
    public int OccupantCount;

    /// <summary>Appraised value, filled by a scan.</summary>
    public int? Value;

    /// <summary>What a fix would restore, filled by a scan: hull tiles gone, parts gone and parts damaged.</summary>
    public WFShipAdminDamage? Damage;

    /// <summary>The fix running on the ship, if any: items done and in all.</summary>
    public int FixDone;
    public int FixTotal;

    /// <summary>The ship's radar colour as hex, and whether it or its label is hidden from radar.</summary>
    public string IffColor = string.Empty;
    public bool IffHidden;
    public bool IffLabelHidden;

    public WFShipAdminProtect Protect;

    /// <summary>How much of their strength the ship's shields have left, in percent; null when it has no shield generator.</summary>
    public int? ShieldPercent;

    /// <summary>Every refit line, with the tier the ship owns.</summary>
    public List<WFShipAdminRefit> Refits = new();
}

/// <summary>Protections staff can switch on a ship.</summary>
[Flags, Serializable, NetSerializable]
public enum WFShipAdminProtect : byte
{
    None = 0,

    /// <summary>Explosions and EMPs don't harm it, and blasts don't break its floors.</summary>
    Blastproof = 1 << 0,

    /// <summary>Nobody aboard can fight.</summary>
    Pacified = 1 << 1,

    /// <summary>The cleanup of abandoned grids leaves it alone.</summary>
    CleanupImmune = 1 << 2,

    /// <summary>Its ship guns don't fire.</summary>
    GunsDisabled = 1 << 3,

    /// <summary>It jumps whatever its mass, with other ships near, and into no-jump zones.</summary>
    FtlUnrestricted = 1 << 4,
}

/// <summary>One refit line as the panel shows it.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminRefit
{
    public string Id = string.Empty;
    public string Name = string.Empty;
    public int Tier;
    public int MaxTier;
}

/// <summary>What a scan found wrong with a ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminDamage(int tiles, int parts, int damaged)
{
    public readonly int Tiles = tiles;
    public readonly int Parts = parts;
    public readonly int Damaged = damaged;
}

/// <summary>What a fix puts right.</summary>
[Flags, Serializable, NetSerializable]
public enum WFShipAdminFixParts : ushort
{
    None = 0,

    /// <summary>Heals every damaged structure and machine.</summary>
    Structures = 1 << 0,

    /// <summary>Re-lays missing hull tiles and rebuilds missing parts from the repair snapshot.</summary>
    Hull = 1 << 1,

    /// <summary>Rebuilds the atmosphere, which also puts tile fires out.</summary>
    Air = 1 << 2,

    /// <summary>Puts burning objects and people out.</summary>
    Fires = 1 << 3,

    /// <summary>Fills batteries and switches breakers on.</summary>
    Power = 1 << 4,

    /// <summary>Refuels generators and reactors.</summary>
    Fuel = 1 << 5,

    /// <summary>Raises door bolts, except on doors the owner sealed.</summary>
    Bolts = 1 << 6,

    /// <summary>Clears stuck pilots and reconnects consoles and gunnery.</summary>
    Consoles = 1 << 7,

    All = Structures | Hull | Air | Fires | Power | Fuel | Bolts | Consoles,
}

/// <summary>The panel's state. The list is only sent when it was rebuilt; null keeps the client's copy.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminEuiState : EuiStateBase
{
    public List<WFShipAdminRow>? Ships;
    public bool Truncated;
    public WFShipAdminFilter Filter;
    public WFShipAdminDetail? Selected;
    public AdminFlags ViewerFlags;

    /// <summary>The outcome of the viewer's last action, and a counter that changes with each one.</summary>
    public string Result = string.Empty;
    public bool ResultError;
    public int ResultSeq;
}

/// <summary>Selects a ship, or none.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminSelectMessage(NetEntity? grid) : EuiMessageBase
{
    public readonly NetEntity? Grid = grid;
}

/// <summary>Rebuilds the list with a filter.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminListMessage(WFShipAdminFilter filter) : EuiMessageBase
{
    public readonly WFShipAdminFilter Filter = filter;
}

/// <summary>What a transfer does with the faction the hull flies for.</summary>
[Serializable, NetSerializable]
public enum WFShipAdminFaction : byte
{
    Keep,

    /// <summary>The faction on the new owner's ID card, or none when the card has none.</summary>
    FromCard,
    Clear,
}

/// <summary>Hands a ship to a player: every earlier owner loses it.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminTransferMessage(NetEntity grid, NetUserId user) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly NetUserId User = user;

    /// <summary>Empties the allow list, so the old owner's guests lose their access too.</summary>
    public bool ClearAllowList;

    /// <summary>Puts each card that held the deed on the allow list, so earlier owners stay aboard as crew.</summary>
    public bool KeepPrevious;

    /// <summary>Stops the ship flying for the old owner's player company.</summary>
    public bool DropCompany = true;

    /// <summary>Takes a crew ship out of Crew mode, so the linked crew's cards stop opening it.</summary>
    public bool LeaveCrewMode = true;

    /// <summary>Takes a company hangar ship away from its company.</summary>
    public bool DetachHangar;
    public WFShipAdminFaction Faction;

    /// <summary>Tells the old and new owners in chat.</summary>
    public bool Notify = true;
}

/// <summary>Adds an account as a co-owner, or removes one owner: an account, or a card or voucher holding the deed.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminOwnerMessage(NetEntity grid, bool add, NetUserId? user, NetEntity? card, bool notify) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly bool Add = add;
    public readonly NetUserId? User = user;
    public readonly NetEntity? Card = card;
    public readonly bool Notify = notify;
}

/// <summary>Sets who besides the owners and the allow list may board.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminModeMessage(NetEntity grid, WFShipAccessMode mode, bool notify) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly WFShipAccessMode Mode = mode;
    public readonly bool Notify = notify;
}

/// <summary>Adds a player's ID card to the allow list, or takes one entry off it.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminAllowMessage(NetEntity grid, NetUserId? add, WFShipAccessKey? remove, bool notify) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly NetUserId? Add = add;
    public readonly WFShipAccessKey? Remove = remove;
    public readonly bool Notify = notify;
}

/// <summary>Starts a fix of the chosen parts.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminFixMessage(NetEntity grid, WFShipAdminFixParts parts) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly WFShipAdminFixParts Parts = parts;
}

/// <summary>Where a move puts the ship.</summary>
[Serializable, NetSerializable]
public enum WFShipAdminMoveKind : byte
{
    /// <summary>Next to the admin.</summary>
    ToMe,

    /// <summary>A position on the map the ship is on.</summary>
    ToPosition,

    /// <summary>Next to another grid.</summary>
    NextTo,

    /// <summary>Docked to another grid, or next to it when no dock fits.</summary>
    DockAt,
}

/// <summary>Moves a ship at once, undocking it first.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminMoveMessage(NetEntity grid, WFShipAdminMoveKind kind, NetEntity? target, Vector2 position) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly WFShipAdminMoveKind Kind = kind;
    public readonly NetEntity? Target = target;
    public readonly Vector2 Position = position;
}

/// <summary>Renames a ship: its grid, station, deeds and shuttle record.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminRenameMessage(NetEntity grid, string name) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly string Name = name;
}

/// <summary>Changes how a ship shows on radar. A null field is left as it is.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminIffMessage(NetEntity grid, string? color, bool? hidden, bool? labelHidden) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly string? Color = color;
    public readonly bool? Hidden = hidden;
    public readonly bool? LabelHidden = labelHidden;
}

/// <summary>Sets the strength the ship's shields have left, in percent. A hundred restores them in full.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminShieldMessage(NetEntity grid, float percent) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly float Percent = percent;
}

/// <summary>Switches one protection on or off.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminProtectMessage(NetEntity grid, WFShipAdminProtect protect, bool on) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly WFShipAdminProtect Protect = protect;
    public readonly bool On = on;
}

/// <summary>Brings a refit line up to a tier, free.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminRefitMessage(NetEntity grid, string refit, int tier) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly string Refit = refit;
    public readonly int Tier = tier;
}

/// <summary>Applies a hull modifier to a ship.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminModifierMessage(NetEntity grid, string modifier) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly string Modifier = modifier;
}

/// <summary>An action on a ship that needs no more than a switch.</summary>
[Serializable, NetSerializable]
public sealed class WFShipAdminActionMessage(NetEntity grid, WFShipAdminAction action, bool value = false, bool notify = false) : EuiMessageBase
{
    public readonly NetEntity Grid = grid;
    public readonly WFShipAdminAction Action = action;
    public readonly bool Value = value;
    public readonly bool Notify = notify;
}
