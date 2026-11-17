using Content.Server._WG.Encounters.Components;
using Content.Shared._WG.Encounters;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>Orders an admin gives one encounter ship from the encounter window.</summary>
public sealed partial class WGEncounterSystem
{
    /// <summary>How far off its target a ship sent to follow or attack keeps.</summary>
    private const float AdminFollowRange = 400f;

    private const float AdminAttackRange = 600f;

    /// <summary>
    /// Has a ship of an encounter carry out an admin's order. <paramref name="target"/> is the ship it is aimed at, and
    /// <paramref name="here"/> where the admin is. Returns why it could not, or null.
    /// </summary>
    public string? AdminOrder(Entity<WGEncounterComponent> encounter, string key, WGEncounterAdminOrder order, EntityUid? target,
        MapCoordinates? here)
    {
        var comp = encounter.Comp;
        if (comp.Resolution != null && order != WGEncounterAdminOrder.SendAway)
            return Loc.GetString("wg-encounter-admin-order-resolved");
        if (!comp.Ships.TryGetValue(key, out var ship) || TerminatingOrDeleted(ship.Grid))
            return Loc.GetString("wg-encounter-admin-gone");
        if (ship.Building)
            return Loc.GetString("wg-encounter-admin-order-building");

        var needsTarget = order is WGEncounterAdminOrder.Extort or WGEncounterAdminOrder.Board or WGEncounterAdminOrder.Attack
            or WGEncounterAdminOrder.Follow;
        EntityUid mark = default;
        if (needsTarget)
        {
            if (target is not { } chosen || TerminatingOrDeleted(chosen) || !HasComp<MapGridComponent>(chosen) || chosen == ship.Grid
                || Transform(chosen).MapID != Transform(ship.Grid).MapID)
                return Loc.GetString("wg-encounter-admin-order-no-target");
            mark = chosen;
        }

        // An encounter still waiting for a player is begun: the admin's order is the start it was waiting for.
        if (!comp.Begun && order is not (WGEncounterAdminOrder.SpawnNow or WGEncounterAdminOrder.SendAway) && !Begin(comp))
            return Loc.GetString("wg-encounter-admin-order-failed");

        switch (order)
        {
            case WGEncounterAdminOrder.Extort:
                if (ship.Extort is not { } extort)
                    return Loc.GetString("wg-encounter-admin-order-not-extorter");
                if (extort.Phase == WGExtortPhase.Board)
                    return Loc.GetString("wg-encounter-admin-order-boarding");
                EndExtortCall(extort);
                extort.Spared.Remove(mark);
                extort.Mark = mark;
                extort.Attempts = 0;
                extort.DeadSince = null;
                SetExtortPhase(extort, WGExtortPhase.Close);
                extort.NextMark = _timing.CurTime + ExtortInterval(extort.Config);
                if (!Shadow(ship, extort, mark))
                    return Loc.GetString("wg-encounter-admin-order-failed");
                break;

            case WGEncounterAdminOrder.Board:
                if (ship.Abduct is { } abduct)
                    return AbductAdminOrder(encounter, key, ship, abduct, mark);
                if (!ship.Hunt || ship.Extort != null)
                    return Loc.GetString("wg-encounter-admin-order-not-hunter");
                ship.Prey = mark;
                ship.Raided = false;
                ship.RaidEnds = null;
                if (_huntRecords.TryGetValue(ship.Grid, out var record))
                {
                    record.Shunned.Remove(mark);
                    record.Misses = 0;
                }
                // Simulated, it flies at its prey as a point; spawned on the way, its crew take the run up.
                if (!SetOrders(encounter.AsNullable(), key, new List<WGCrewObjective>
                    {
                        new() { Kind = WGCrewObjectiveKind.Follow, Target = GetNetEntity(mark), Range = AdminFollowRange },
                    }))
                    return Loc.GetString("wg-encounter-admin-order-failed");
                break;

            case WGEncounterAdminOrder.Attack:
            case WGEncounterAdminOrder.Follow:
                // An extorter or boarder set on something else lets its own run go.
                if (ship.Extort is { } extorting)
                {
                    EndExtortCall(extorting);
                    extorting.Mark = null;
                    SetExtortPhase(extorting, WGExtortPhase.Roam);
                    extorting.NextMark = _timing.CurTime + ExtortInterval(extorting.Config);
                }

                if (ship.Abduct is { } abducting)
                    AbductAdminCancel(abducting);

                ship.Prey = null;
                var attack = order == WGEncounterAdminOrder.Attack;
                if (!SetOrders(encounter.AsNullable(), key, new List<WGCrewObjective>
                    {
                        new()
                        {
                            Kind = attack ? WGCrewObjectiveKind.Attack : WGCrewObjectiveKind.Follow,
                            Target = GetNetEntity(mark),
                            Range = attack ? AdminAttackRange : AdminFollowRange,
                        },
                        new() { Kind = WGCrewObjectiveKind.Hold },
                    }))
                    return Loc.GetString("wg-encounter-admin-order-failed");
                if (attack)
                {
                    var ordered = new WGEncounterAdminAttackEvent(encounter.Owner, key, ship.Grid, mark);
                    RaiseLocalEvent(ref ordered);
                }

                break;

            case WGEncounterAdminOrder.ComeToMe:
                if (here is not { } point || point.MapId != Transform(ship.Grid).MapID)
                    return Loc.GetString("wg-encounter-admin-order-not-here");
                if (!SetOrders(encounter.AsNullable(), key, new List<WGCrewObjective>
                    {
                        new() { Kind = WGCrewObjectiveKind.GoTo, Position = point.Position, Range = 300f },
                        new() { Kind = WGCrewObjectiveKind.Hold },
                    }))
                    return Loc.GetString("wg-encounter-admin-order-failed");
                break;

            case WGEncounterAdminOrder.Hold:
                if (!SetOrders(encounter.AsNullable(), key, new List<WGCrewObjective> { new() { Kind = WGCrewObjectiveKind.Hold } }))
                    return Loc.GetString("wg-encounter-admin-order-failed");
                break;

            case WGEncounterAdminOrder.Resume:
                if (!_prototypes.TryIndex(comp.Prototype, out var prototype)
                    || prototype.Ships.Find(entry => entry.Key == key) is not { } entry
                    || !IssueShipOrders(comp, prototype, entry, ship))
                    return Loc.GetString("wg-encounter-admin-order-failed");
                break;

            case WGEncounterAdminOrder.SpawnNow:
                if (!ship.Virtual)
                    return Loc.GetString("wg-encounter-admin-order-not-simulated");
                _pending.RemoveAll(entry => entry.Encounter == encounter.Owner && entry.Key == key);
                _pending.Insert(0, (encounter.Owner, key, true));
                break;

            case WGEncounterAdminOrder.SendAway:
                // An admin's own encounter is kept until ended; sent away, it goes like any other.
                comp.Pinned = false;
                if (comp.Resolution == null)
                    Resolve(encounter.AsNullable(), WGEncounterResolution.Completed);
                comp.JumpAt = _timing.CurTime;
                break;
        }

        return null;
    }
}
