using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared._WF.RMCCore.Slow;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Drunk;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Applies the effects of <see cref="FaunaCloudComponent"/> clouds to the mobs inside them.
/// </summary>
public sealed partial class FaunaCloudSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private RMCSlowSystem _slow = default!;
    [Dependency] private SharedInternalsSystem _internals = default!;
    [Dependency] private SharedDrunkSystem _drunk = default!;
    [Dependency] private StaminaSystem _stamina = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaCloudComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.Interval;
            _mobs.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, comp.Radius, _mobs);
            foreach (var mob in _mobs)
            {
                if (_mobState.IsDead(mob, mob.Comp)
                    || comp.SparedFaction is { } spared && _faction.IsMember(mob.Owner, spared)
                    || comp.BreathingOnly
                    && (!HasComp<RespiratorComponent>(mob) || _internals.AreInternalsWorking(mob)))
                    continue;

                if (comp.Damage != null)
                    _damageable.TryChangeDamage(mob, comp.Damage);

                if (comp.Stamina > 0)
                    _stamina.TakeStaminaDamage(mob, comp.Stamina);

                if (comp.Slow > TimeSpan.Zero)
                    _slow.TrySlowdown(mob, comp.Slow, ignoreDurationModifier: true);

                if (comp.Disorient > 0)
                    _drunk.TryApplyDrunkenness(mob, comp.Disorient, false);

                if (comp.FireStacks > 0)
                {
                    _flammable.AdjustFireStacks(mob, comp.FireStacks);
                    _flammable.Ignite(mob, uid);
                }
            }
        }
    }
}
