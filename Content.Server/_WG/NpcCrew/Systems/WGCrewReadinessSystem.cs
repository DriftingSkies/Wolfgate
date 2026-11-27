using Content.Server._WG.CrewBarks;
using Content.Server._WG.NpcCrew.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipPa;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>How ready a ship's crew stands.</summary>
public enum WGCrewReadiness : byte
{
    /// <summary>Code Green: duties, chores and breaks as usual.</summary>
    Normal,

    /// <summary>Code Yellow: on duty, no breaks.</summary>
    Alert,

    /// <summary>General quarters, Code Red or Black: everyone to their station, the fighters with weapons out.</summary>
    Battle,
}

/// <summary>
/// Reads a ship's readiness from its situation code and general quarters (ShipPa) and has its crew answer a change:
/// breaks end, plans are redone, and they say so.
/// </summary>
public sealed class WGCrewReadinessSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewBarkSystem _barks = default!;

    private static readonly ProtoId<ShipAlertCodePrototype> Green = "WGShipCodeGreen";
    private static readonly ProtoId<ShipAlertCodePrototype> Yellow = "WGShipCodeYellow";
    private static readonly ProtoId<ShipAlertCodePrototype> Red = "WGShipCodeRed";
    private static readonly ProtoId<ShipAlertCodePrototype> Black = "WGShipCodeBlack";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipGeneralQuartersChangedEvent>(OnQuarters);
        SubscribeLocalEvent<ShipAlertCodeChangedEvent>(OnCode);
    }

    /// <summary>The readiness a ship's code and general quarters call for; Normal on a ship with no PA.</summary>
    public WGCrewReadiness Readiness(EntityUid? grid)
    {
        if (grid == null || !TryComp<ShipAlertComponent>(grid, out var alert))
            return WGCrewReadiness.Normal;
        return Of(alert.Code, alert.GeneralQuarters);
    }

    private static WGCrewReadiness Of(ProtoId<ShipAlertCodePrototype> code, bool quarters)
    {
        if (quarters || code == Red || code == Black)
            return WGCrewReadiness.Battle;
        return code == Yellow ? WGCrewReadiness.Alert : WGCrewReadiness.Normal;
    }

    /// <summary>The readiness of the ship a crewman serves.</summary>
    public WGCrewReadiness ReadinessOf(EntityUid uid) => Readiness(_crew.HomeGrid(uid));

    /// <summary>Whether a crewman belongs at his battle station now; the helm and the guns keep their own duty.</summary>
    public bool AtStations(EntityUid uid)
    {
        return TryComp<WGCrewComponent>(uid, out var crew) && crew.Duty is not (WGCrewDuties.Pilot or WGCrewDuties.Gunnery)
            && Readiness(_crew.HomeGrid(uid, crew)) == WGCrewReadiness.Battle;
    }

    /// <summary>Whether he stands at his station with his weapon out: at stations, and one who fights on sight.</summary>
    public bool StandsToArms(EntityUid uid)
    {
        return AtStations(uid) && TryComp<WGCrewComponent>(uid, out var crew) && crew.Engagement == WGCrewEngagement.OnSight;
    }

    private void OnQuarters(ref ShipGeneralQuartersChangedEvent ev)
    {
        var code = CompOrNull<ShipAlertComponent>(ev.Grid)?.Code ?? Green;
        Changed(ev.Grid, Of(code, !ev.Active), Of(code, ev.Active));
    }

    private void OnCode(ref ShipAlertCodeChangedEvent ev)
    {
        var quarters = CompOrNull<ShipAlertComponent>(ev.Grid)?.GeneralQuarters ?? false;
        Changed(ev.Grid, Of(ev.Old, quarters), Of(ev.New, quarters));
    }

    /// <summary>The crew aboard answer a change of readiness: breaks end, everyone replans, and they acknowledge it.</summary>
    private void Changed(EntityUid grid, WGCrewReadiness before, WGCrewReadiness after)
    {
        if (before == after)
            return;

        var bark = after switch
        {
            WGCrewReadiness.Battle => WGCrewBark.Stations,
            WGCrewReadiness.Alert => WGCrewBark.Alert,
            _ => WGCrewBark.StandDown,
        };
        var downtime = EntityManager.System<WGCrewDowntimeSystem>();
        var query = EntityQueryEnumerator<WGCrewComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var htn, out var xform))
        {
            if (_crew.HomeGrid(uid, crew) != grid || xform.GridUid != grid || !_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid))
                continue;
            if (after != WGCrewReadiness.Normal)
                downtime.EndBreak(uid);
            _htn.Replan(htn);
            // A medic with a patient on his hands keeps his mind on them.
            if (CompOrNull<WGCrewMedicComponent>(uid)?.Patient == null)
                _barks.Bark(uid, bark);
        }
    }
}
