using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.SpecialistKits;

/// <summary>On a specialist kit's vendor bundle: its per-faction limit, if any.</summary>
[RegisterComponent]
public sealed partial class WFSpecialistKitComponent : Component
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
public record struct WFSpecialistKitVendAttemptEvent(EntityUid User, EntProtoId Entry)
{
    public bool Cancelled;

    /// <summary>Why it was refused, shown to the user.</summary>
    public LocId Reason = "wf-specialist-kit-full";
}
