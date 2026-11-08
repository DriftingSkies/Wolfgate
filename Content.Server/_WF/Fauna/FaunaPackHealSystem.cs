using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Heals hurt packmates or faction members around each living <see cref="FaunaPackHealComponent"/> mob.
/// </summary>
public sealed partial class FaunaPackHealSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    private readonly HashSet<Entity<MobStateComponent>> _pack = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaPackHealComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.Interval;
            if (_mobState.IsIncapacitated(uid))
                continue;

            _pack.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, comp.Radius, _pack);
            foreach (var mate in _pack)
            {
                if (mate.Owner == uid
                    || _mobState.IsDead(mate, mate.Comp)
                    || (comp.Faction is { } faction
                        ? !_faction.IsMember(mate.Owner, faction)
                        : !HasComp<FaunaPackHealComponent>(mate))
                    || !TryComp<DamageableComponent>(mate, out var damageable)
                    || damageable.TotalDamage <= 0)
                    continue;

                _damageable.TryChangeDamage(mate.Owner, comp.Heal, true, false, damageable);
                _audio.PlayPvs(comp.Sound, mate);
            }
        }
    }
}
