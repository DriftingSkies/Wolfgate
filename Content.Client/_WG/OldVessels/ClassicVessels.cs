using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Tag;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.OldVessels;

/// <summary>
/// The tags that mark the classic ships Monolith#4624 removed and Wolfgate kept.
/// </summary>
public static class ClassicVessels
{
    /// <summary>Every classic ship, including the ones back on sale.</summary>
    public static readonly ProtoId<TagPrototype> ClassicTag = "WGClassicVessel";

    /// <summary>Classic ships no shipyard sells; only admins spawn them.</summary>
    public static readonly ProtoId<TagPrototype> AdminOnlyTag = "WGAdminOnlyVessel";

    /// <summary>Whether the vessel is a classic ship kept from before Monolith#4624.</summary>
    public static bool IsClassic(VesselPrototype vessel)
    {
        return vessel.Tags.Contains(ClassicTag);
    }

    /// <summary>Whether the classic vessel is admin spawn only.</summary>
    public static bool IsAdminOnly(VesselPrototype vessel)
    {
        return vessel.Tags.Contains(AdminOnlyTag);
    }
}
