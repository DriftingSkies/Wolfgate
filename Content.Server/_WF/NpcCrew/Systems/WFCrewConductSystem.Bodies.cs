using Content.Server._WF.NpcCrew.Components;
using Content.Server.Carrying;
using Content.Shared.Movement.Pulling.Components;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// A player seen hauling one of the crew's downed or dead, by pulling or carrying, is told to put him down and has
/// <see cref="HaulTime"/> to do it before the crew turn on him. The ship's command can let him, as with a gun.
/// </summary>
public sealed partial class WFCrewConductSystem
{
    /// <summary>How long a player told to put a body down has to do it.</summary>
    public static readonly TimeSpan HaulTime = TimeSpan.FromSeconds(10);

    /// <summary>Players told to put a body down, by ship and crew: when their time runs out, and whether it has.</summary>
    private readonly Dictionary<(EntityUid Grid, string Group, EntityUid Player), (TimeSpan Deadline, bool Turned)> _hauling = new();

    private readonly Dictionary<(EntityUid Grid, string Group, EntityUid Player), EntityUid> _haulSeen = new();
    private readonly HashSet<(EntityUid Grid, string Group, EntityUid Player)> _haulNow = new();

    /// <summary>The downed crewman a player is pulling or carrying, if any.</summary>
    private EntityUid? Hauled(EntityUid player)
    {
        if (TryComp<PullerComponent>(player, out var puller) && puller.Pulling is { } pulled && DownedCrew(pulled))
            return pulled;
        if (TryComp<CarryingComponent>(player, out var carrying) && DownedCrew(carrying.Carried))
            return carrying.Carried;
        return null;

        bool DownedCrew(EntityUid body) => HasComp<WFCrewComponent>(body) && !_mobs.IsAlive(body);
    }

    /// <summary>The half-second pass over players hauling the crew's downed.</summary>
    private void UpdateHauling(TimeSpan now)
    {
        _haulNow.Clear();
        _haulSeen.Clear();
        var players = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (players.MoveNext(out var player, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || !_mobs.IsAlive(player) || Hauled(player) is not { } body
                || !TryComp<WFCrewComponent>(body, out var bodyCrew))
                continue;

            var key = (grid, bodyCrew.Group, player);
            var crew = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
            while (crew.MoveNext(out var uid, out var member, out var crewXform))
            {
                if (crewXform.GridUid != grid || !_crew.SameCrew(uid, body) || !_mobs.IsAlive(uid)
                    || HasComp<ActorComponent>(uid) || _crew.SameCrew(uid, player) || Commanded(uid, player))
                    continue;

                _haulNow.Add(key);
                if (_weapons.CanSee(uid, player))
                {
                    _haulSeen.TryAdd(key, uid);
                    break;
                }
            }
        }

        foreach (var (key, speaker) in _haulSeen)
        {
            if (_hauling.TryAdd(key, (now + HaulTime, false)))
                Say(speaker, "wf-crew-conduct-haul-warn");
        }

        foreach (var key in new List<(EntityUid Grid, string Group, EntityUid Player)>(_hauling.Keys))
        {
            var warning = _hauling[key];
            if (warning.Turned)
            {
                if (TerminatingOrDeleted(key.Player) || Transform(key.Player).GridUid != key.Grid)
                    _hauling.Remove(key);
                continue;
            }

            if (!_haulNow.Contains(key))
            {
                if (!TerminatingOrDeleted(key.Player) && Transform(key.Player).GridUid == key.Grid && Seer(key) is { } seer)
                    Say(seer, "wf-crew-conduct-haul-complied");
                _hauling.Remove(key);
                continue;
            }

            if (now < warning.Deadline || !_haulSeen.TryGetValue(key, out var speaker))
                continue;

            _hauling[key] = (warning.Deadline, true);
            Say(speaker, "wf-crew-conduct-haul-hostile");
            _crew.AnswerAttack(key.Grid, key.Group, key.Player);
            Log.Info($"{ToPrettyString(key.Player)} kept hauling a downed crewman aboard {ToPrettyString(key.Grid)}; crew {key.Group} turned hostile.");
        }
    }
}
