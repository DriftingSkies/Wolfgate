using Content.Server.Beam;
using Content.Shared.Examine;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Attaches, pulls and breaks the vines of <see cref="FaunaVineComponent"/> mobs.
/// </summary>
public sealed partial class FaunaVineSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BeamSystem _beam = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStunSystem _stun = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(0.25);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaVineComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!_mobState.IsAlive(uid))
            {
                comp.Targets.Clear();
                continue;
            }

            var origin = _transform.GetWorldPosition(uid);
            if (comp.Targets.Count > 0)
            {
                comp.Targets.RemoveAll(target => TerminatingOrDeleted(target)
                    || !_mobState.IsAlive(target)
                    || (_transform.GetWorldPosition(target) - origin).Length() is var distance
                    && (distance < 1.2f || distance > comp.Range + 1));
            }

            if (now >= comp.NextAttach)
            {
                if (comp.Targets.Count < comp.MaxVines && TryAttach((uid, comp)))
                {
                    comp.NextAttach = now + comp.AttachCooldown;
                }
                else if (comp.YankAll && comp.Targets.Count > 0)
                {
                    comp.NextAttach = now + comp.AttachCooldown / 2;
                    comp.NextPull = now;
                }
                else if (comp.Targets.Count < comp.MaxVines)
                {
                    comp.NextAttach = now + RetryDelay;
                }
            }

            if (now < comp.NextPull)
                continue;

            comp.NextPull = now + comp.PullInterval;
            foreach (var target in comp.Targets)
            {
                var toward = origin - _transform.GetWorldPosition(target);
                if (toward.LengthSquared() > 0.01f)
                    _throwing.TryThrow(target, toward.Normalized(), 5f, uid, pushbackRatio: 0f);

                _beam.TryCreateBeam(uid, target, comp.Beam);
            }
        }
    }

    private bool TryAttach(Entity<FaunaVineComponent> ent)
    {
        foreach (var target in _faction.GetNearbyHostiles(ent.Owner, ent.Comp.Range))
        {
            if (ent.Comp.Targets.Contains(target)
                || !_mobState.IsAlive(target)
                || !_examine.InRangeUnOccluded(ent.Owner, target, ent.Comp.Range))
                continue;

            ent.Comp.Targets.Add(target);
            _stun.TryKnockdown(target, ent.Comp.Knockdown, true);
            _beam.TryCreateBeam(ent, target, ent.Comp.Beam);
            if (ent.Comp.Popup is { } popup)
                _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner), ("target", target)), target, PopupType.MediumCaution);

            return true;
        }

        return false;
    }
}
