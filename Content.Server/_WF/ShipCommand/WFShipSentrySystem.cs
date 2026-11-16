using System.Linq;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared._WF.ShipCommand;

namespace Content.Server._WF.ShipCommand;

/// <summary>
/// Points a commanded ship's sentries at whoever its crew turn on: a trespasser who stayed past his warning, a thief, an
/// attacker. Every couple of seconds each sentry lets go of targets that are down or have left the ship, and of
/// trespassers its crew's boarding rule no longer marks hostile.
/// </summary>
public sealed class WFShipSentrySystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private WFCrewSecuritySystem _security = default!;

    private float _timer;

    /// <summary>Sentry targets raised by a boarding sighting alone, not by an attack.</summary>
    private readonly HashSet<(EntityUid Sentry, EntityUid Target)> _sighted = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFShipCommandComponent, WFCrewAttackAnsweredEvent>(OnAttackAnswered);
    }

    private void OnAttackAnswered(Entity<WFShipCommandComponent> grid, ref WFCrewAttackAnsweredEvent args)
    {
        var query = EntityQueryEnumerator<WFShipSentryComponent, TransformComponent>();
        while (query.MoveNext(out var sentry, out _, out var xform))
        {
            if (xform.GridUid != grid.Owner)
                continue;

            var key = (sentry, args.Attacker);
            if (!args.Sighting)
                _sighted.Remove(key);
            else if (_sighted.Contains(key) || !HostileTo(sentry, args.Attacker))
                _sighted.Add(key);
            else
                continue; // Already hostile from an attack, which a sighting doesn't make releasable.

            _factions.AggroEntity(sentry, args.Attacker);
        }
    }

    private bool HostileTo(EntityUid sentry, EntityUid target)
    {
        return TryComp<FactionExceptionComponent>(sentry, out var exceptions) && exceptions.Hostiles.Any(t => t == target);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 2f)
            return;

        _timer = 0f;
        _sighted.RemoveWhere(pair => TerminatingOrDeleted(pair.Sentry));
        var query = EntityQueryEnumerator<WFShipSentryComponent, FactionExceptionComponent, TransformComponent>();
        while (query.MoveNext(out var sentry, out _, out var exceptions, out var xform))
        {
            foreach (var target in new List<EntityUid>(exceptions.Hostiles))
            {
                if (TerminatingOrDeleted(target) || !_mobs.IsAlive(target) || Transform(target).GridUid != xform.GridUid
                    || _sighted.Contains((sentry, target)) && xform.GridUid is { } grid && !_security.OwnsHostile(grid, target))
                {
                    _factions.DeAggroEntity(sentry, target);
                    _sighted.Remove((sentry, target));
                }
            }
        }
    }
}
