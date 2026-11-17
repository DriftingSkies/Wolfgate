using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Synthetics;

/// <summary>The body's own action: open the chassis panel.</summary>
public sealed partial class WGOpenChassisPanelActionEvent : InstantActionEvent;

/// <summary>Opening or closing a chassis bay, by its owner or by someone else.</summary>
[Serializable, NetSerializable]
public sealed partial class WGChassisBayDoAfterEvent : SimpleDoAfterEvent
{
    public readonly bool Open;

    public WGChassisBayDoAfterEvent(bool open)
    {
        Open = open;
    }
}

/// <summary>Pulling a component out of an open bay by hand.</summary>
[Serializable, NetSerializable]
public sealed partial class WGChassisPullOrganDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Organ;

    public WGChassisPullOrganDoAfterEvent(NetEntity organ)
    {
        Organ = organ;
    }
}

/// <summary>Seating a component into an open bay's empty slot by hand.</summary>
[Serializable, NetSerializable]
public sealed partial class WGChassisInstallOrganDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Organ;
    public readonly NetEntity Part;
    public readonly string Slot;

    public WGChassisInstallOrganDoAfterEvent(NetEntity organ, NetEntity part, string slot)
    {
        Organ = organ;
        Part = part;
        Slot = slot;
    }
}

/// <summary>Clicking a loose chassis limb back onto the body it came off.</summary>
[Serializable, NetSerializable]
public sealed partial class WGLimbReattachDoAfterEvent : SimpleDoAfterEvent
{
    public readonly NetEntity Limb;
    public readonly NetEntity Parent;
    public readonly string Slot;

    public WGLimbReattachDoAfterEvent(NetEntity limb, NetEntity parent, string slot)
    {
        Limb = limb;
        Parent = parent;
        Slot = slot;
    }
}
