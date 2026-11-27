using Content.Server._WG.NpcCrew.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Picks vermin for pest hunters to chase down; <c>WGCrewPestOperator</c> hands the pick to the HTN.</summary>
public sealed class WGCrewPestSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCrewWeaponSystem _weapons = default!;

    /// <summary>The blackboard key the hunted pest is held under.</summary>
    public const string PestKey = "WGCrewPest";

    private float _timer;

    /// <summary>The pest a hunter is after and where it is, if he is after one.</summary>
    public bool TryGetPest(EntityUid hunter, out EntityUid pest, out EntityCoordinates coordinates)
    {
        pest = default;
        coordinates = default;
        if (!TryComp<WGCrewPestHunterComponent>(hunter, out var comp) || comp.Target is not { } target)
            return false;
        if (!StillHunting(hunter, comp, target))
        {
            comp.Target = null;
            return false;
        }
        pest = target;
        coordinates = Transform(target).Coordinates;
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _timer += frameTime;
        if (_timer < 1f)
            return;
        _timer = 0f;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WGCrewPestHunterComponent, WGCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hunter, out var crew, out var xform))
        {
            if (hunter.Target is { } current && !StillHunting(uid, hunter, current))
                hunter.Target = null;
            if (hunter.Target != null || now < hunter.NextLook)
                continue;
            hunter.NextLook = now + hunter.Interval;
            if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || _weapons.HasLiveThreat(uid)
                || crew.Post?.EntityId is not { } home || xform.GridUid != home || !_random.Prob(hunter.Chance))
                continue;
            if (FindPest(uid, hunter, xform) is { } pest)
            {
                hunter.Target = pest;
                hunter.Until = now + hunter.HuntTime;
            }
        }
    }

    /// <summary>Whether a hunt is still worth going on with: the pest alive, on his ship, and time left.</summary>
    private bool StillHunting(EntityUid hunter, WGCrewPestHunterComponent comp, EntityUid target)
    {
        return _timing.CurTime < comp.Until && !TerminatingOrDeleted(target) && _mobs.IsAlive(target)
            && _mobs.IsAlive(hunter) && Transform(target).GridUid == Transform(hunter).GridUid;
    }

    /// <summary>Whether another hunter is after this pest now, his hunt not yet run out: one hunter to a pest.</summary>
    private bool Hunted(EntityUid hunter, EntityUid pest)
    {
        var now = _timing.CurTime;
        var others = EntityQueryEnumerator<WGCrewPestHunterComponent>();
        while (others.MoveNext(out var other, out var comp))
        {
            if (other != hunter && comp.Target == pest && now < comp.Until)
                return true;
        }
        return false;
    }

    /// <summary>The nearest pest he can see within range that nobody else is already after.</summary>
    private EntityUid? FindPest(EntityUid uid, WGCrewPestHunterComponent hunter, TransformComponent xform)
    {
        EntityUid? best = null;
        var distance = hunter.Range * hunter.Range;
        var here = _transform.GetWorldPosition(xform);
        var mobs = EntityQueryEnumerator<Content.Shared.Mobs.Components.MobStateComponent, MetaDataComponent, TransformComponent>();
        while (mobs.MoveNext(out var other, out _, out var meta, out var there))
        {
            if (there.GridUid != xform.GridUid || meta.EntityPrototype is not { } proto
                || !hunter.Pests.Contains(proto.ID) || !_mobs.IsAlive(other) || HasComp<ActorComponent>(other))
                continue;
            var squared = (_transform.GetWorldPosition(there) - here).LengthSquared();
            if (squared >= distance || !_weapons.CanSee(uid, other) || Hunted(uid, other))
                continue;
            best = other;
            distance = squared;
        }
        return best;
    }
}
