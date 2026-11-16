using Content.Server._WF.NpcCrew.Components;
using Content.Server.Chat.Systems;
using Content.Server.Pointing.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.IdentityManagement;
using Robust.Shared.Player;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// The crew challenge a trespasser aloud: the crewman who sees him names him and where he stands, points him out and
/// gives him his time; a last warning near the end; and "engaging" when the fighters turn on him.
/// </summary>
public sealed partial class WFCrewSecuritySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private PointingSystem _pointing = default!;

    /// <summary>How long before the deadline the last warning is given.</summary>
    private static readonly TimeSpan LastWarningLead = TimeSpan.FromSeconds(10);

    /// <summary>Warned strangers who have had their last warning.</summary>
    private readonly HashSet<(EntityUid Grid, string Group, EntityUid Visitor)> _lastWarned = new();

    private void Challenge(EntityUid crew, EntityUid visitor, EntityUid grid, TimeSpan time)
    {
        Shout(crew, visitor, "wf-crew-challenge-warn", ("place", PlaceName(grid, visitor)),
            ("seconds", (int) Math.Round(time.TotalSeconds)));
    }

    /// <summary>The 1 Hz pass's last warnings, for strangers whose time is nearly up.</summary>
    private void LastWarnings(HashSet<(EntityUid Grid, string Group, EntityUid Visitor)> seen)
    {
        var now = _timing.CurTime;
        _lastWarned.RemoveWhere(key => !_warned.ContainsKey(key));
        foreach (var (key, warning) in _warned)
        {
            if (warning.Turned || now < warning.Deadline - LastWarningLead || !seen.Contains(key)
                || _lastWarned.Contains(key) || Seer(key) is not { } crew)
                continue;

            _lastWarned.Add(key);
            Shout(crew, key.Visitor, "wf-crew-challenge-last");
        }
    }

    /// <summary>Called as the fighters turn on a stranger who stayed: one of the crew who sees him says so.</summary>
    private void Engaging((EntityUid Grid, string Group, EntityUid Visitor) key)
    {
        if (Seer(key) is { } crew)
            Shout(crew, key.Visitor, "wf-crew-challenge-engage");
    }

    /// <summary>A living crewman of the group, on the grid, who can see the visitor.</summary>
    private EntityUid? Seer((EntityUid Grid, string Group, EntityUid Visitor) key)
    {
        var query = EntityQueryEnumerator<WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var crew, out var xform))
        {
            if (xform.GridUid == key.Grid && crew.Group == key.Group && _mobs.IsAlive(uid)
                && !HasComp<ActorComponent>(uid) && _weapons.CanSee(uid, key.Visitor))
                return uid;
        }

        return null;
    }

    private void Shout(EntityUid crew, EntityUid visitor, string line, params (string, object)[] args)
    {
        var all = new List<(string, object)>(args) { ("visitor", Identity.Name(visitor, EntityManager)) };
        _pointing.TryNpcPoint(crew, visitor);
        _chat.TrySendInGameICMessage(crew, Loc.GetString(line, all.ToArray()), InGameICChatType.Speak,
            hideChat: false, checkRadioPrefix: false);
    }

    /// <summary>What the ship calls the place the visitor stands in, or "this ship".</summary>
    private string PlaceName(EntityUid grid, EntityUid visitor)
    {
        var ev = new WFCrewPlaceNameEvent(visitor);
        RaiseLocalEvent(grid, ref ev);
        return ev.Name ?? Loc.GetString("wf-crew-challenge-place-ship");
    }
}

/// <summary>Raised on a crew's grid to name the place a visitor stands in, as a zone's name, for the crew's challenge.</summary>
[ByRefEvent]
public record struct WFCrewPlaceNameEvent(EntityUid Visitor, string? Name = null);
