using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.ShipRepair;

/// <summary>The SRD's reattach do-after, raised on the tool.</summary>
[Serializable, NetSerializable]
public sealed partial class WGHullReattachDoAfterEvent : SimpleDoAfterEvent
{
    /// <summary>The section being reattached.</summary>
    public NetEntity Section;

    public override bool IsDuplicate(DoAfterEvent other)
    {
        return other is WGHullReattachDoAfterEvent cast && cast.Section == Section;
    }
}

/// <summary>Raised on a section, server side, once a reattach passed its checks; the handler merges it into the hull.</summary>
[ByRefEvent]
public readonly record struct WGHullReattachEvent(EntityUid Hull);
