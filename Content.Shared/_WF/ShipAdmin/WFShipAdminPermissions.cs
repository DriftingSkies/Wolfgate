using Content.Shared.Administration;

namespace Content.Shared._WF.ShipAdmin;

/// <summary>Everything the admin ship panel can do to a ship.</summary>
public enum WFShipAdminAction : byte
{
    Scan,

    Transfer,
    AddOwner,
    RemoveOwner,
    Release,
    DetachHangar,

    SetLocked,
    SetMode,
    AllowAdd,
    AllowRemove,
    AllowClear,
    ResetDoors,

    Fix,
    Snapshot,
    Restore,

    BringToMe,
    SendTo,
    DockAt,
    Undock,
    Halt,
    ClearPilots,
    FtlLock,
    ResetFtl,

    Rename,
    Iff,
    Shields,
    Power,
    Protect,

    Refit,
    Fitting,
    Modifier,

    Eject,
    Strip,
    Delete,
}

/// <summary>The one table of which admin flag each panel action needs. The server checks it and the window greys buttons by it.</summary>
public static class WFShipAdminPermissions
{
    /// <summary>The flag that opens the panel and that every action needs.</summary>
    public const AdminFlags Base = AdminFlags.Admin;

    /// <summary>The flags an action needs. Every action runs on the staff flag; the hard ones to undo ask twice instead.</summary>
    public static AdminFlags Required(WFShipAdminAction action)
    {
        return Base;
    }

    /// <summary>Whether someone holding these flags may run the action.</summary>
    public static bool Allowed(AdminFlags held, WFShipAdminAction action)
    {
        var required = Required(action);
        return (held & required) == required;
    }
}
