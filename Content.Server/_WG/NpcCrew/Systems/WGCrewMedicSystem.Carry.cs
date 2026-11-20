using Content.Server.Carrying;
using Content.Shared.Movement.Pulling.Components;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// Medics carry the people they take in rather than drag them along the deck; a pull is the fallback when a carry
/// can't be had (hands full, or a body nobody can carry).
/// </summary>
public sealed partial class WGCrewMedicSystem
{
    [Dependency] private CarryingSystem _carrying = default!;

    /// <summary>Picks the patient up, or takes hold of them to pull when that fails. True when he has them either way.</summary>
    private bool TakeHold(EntityUid mob, EntityUid patient)
    {
        if (Drags(mob, patient))
            return true;

        return _carrying.TryCrewCarry(mob, patient) || _pulling.TryStartPull(mob, patient);
    }

    /// <summary>Sets the patient down, or lets go of the pull, if this medic has them.</summary>
    private void LetGo(EntityUid mob, EntityUid patient)
    {
        if (CompOrNull<BeingCarriedComponent>(patient)?.Carrier == mob)
            _carrying.DropCarried(mob, patient);

        if (TryComp<PullableComponent>(patient, out var pullable) && pullable.Puller == mob)
            _pulling.TryStopPull(patient, pullable, mob);
    }

    /// <summary>Whether this medic carries or pulls the patient.</summary>
    private bool Drags(EntityUid mob, EntityUid patient)
    {
        return CompOrNull<BeingCarriedComponent>(patient)?.Carrier == mob || CompOrNull<PullableComponent>(patient)?.Puller == mob;
    }

    /// <summary>Whether someone other than this medic carries or pulls the patient.</summary>
    private bool HeldByOther(EntityUid mob, EntityUid patient)
    {
        return CompOrNull<BeingCarriedComponent>(patient)?.Carrier is { } carrier && carrier != mob
               || CompOrNull<PullableComponent>(patient)?.Puller is { } puller && puller != mob;
    }
}
