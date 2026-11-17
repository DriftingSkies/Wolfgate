using Content.Server._WG.NpcCrew.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._WG.NpcCrew;

namespace Content.Server._WG.NpcCrew.Systems;

public sealed partial class WGCrewAlertSystem
{
    [Dependency] private DockingSystem _dockedShips = default!;

    /// <summary>
    /// A shot at a docked ship counts as one at the ship it is docked to, when that ship's gunners defend their docks:
    /// once per ship, however many ports join them.
    /// </summary>
    private void ReportDockedAttack(EntityUid victim, EntityUid attacker)
    {
        // A local set: reporting the attack raises events that may land another hit here.
        HashSet<EntityUid>? hosts = null;
        foreach (var dock in _dockedShips.GetDocks(victim))
        {
            if (dock.Comp.DockedWith is not { } other || Transform(other).GridUid is not { } host || host == victim || host == attacker
                || CompOrNull<WGGunnerPolicyComponent>(host)?.Policy != WGGunnerPolicy.DefendDocked)
                continue;

            hosts ??= new HashSet<EntityUid>();
            hosts.Add(host);
        }

        if (hosts == null)
            return;

        foreach (var host in hosts)
        {
            if (!Excuse(host, attacker))
                ReportAttack(host, attacker, gunfire: true);
        }
    }
}
