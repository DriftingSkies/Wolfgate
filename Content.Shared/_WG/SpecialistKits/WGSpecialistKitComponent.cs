using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.SpecialistKits;

/// <summary>On a specialist kit's vendor bundle: its per-faction limit, if any.</summary>
[RegisterComponent]
public sealed partial class WGSpecialistKitComponent : Component
{
    /// <summary>
    /// Whether the kit is limited per faction: 1, plus 1 per <see cref="MembersPerSlot"/> members online, up to
    /// <see cref="MaxSlots"/>. Job-locked kits leave this off; the job's slots cap them.
    /// </summary>
    [DataField]
    public bool Limited = true;

    [DataField]
    public int MembersPerSlot = 10;

    [DataField]
    public int MaxSlots = 3;

    /// <summary>The jobs that may take it; empty for anyone.</summary>
    [DataField]
    public List<ProtoId<JobPrototype>> Jobs = new();
}

/// <summary>Raised (broadcast) when a vendor is about to hand out an entry; cancel to refuse it.</summary>
[ByRefEvent]
public record struct WGSpecialistKitVendAttemptEvent(EntityUid User, EntProtoId Entry)
{
    public bool Cancelled;

    /// <summary>Why it was refused, shown to the user.</summary>
    public LocId Reason = "wg-specialist-kit-full";
}

/// <summary>Raised (broadcast) once a vendor has handed out an entry, after every check of its own passed.</summary>
[ByRefEvent]
public record struct WGSpecialistKitVendedEvent(EntityUid User, EntProtoId Entry);
