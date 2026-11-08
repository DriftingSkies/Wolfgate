using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Feeds <see cref="FaunaHungerDrainComponent"/> mobs from their melee targets and buds them when full.
/// </summary>
public sealed partial class FaunaHungerDrainSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaHungerDrainComponent, MeleeHitEvent>(OnMeleeHit);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaHungerDrainComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextTick)
                continue;

            comp.NextTick = now + comp.TickInterval;
            if (comp.Stored < comp.BudAt || now < comp.NextBud || !_mobState.IsAlive(uid))
                continue;

            if (!_random.Prob(comp.BudChance) || CountAlive(uid) >= comp.MaxAlive)
                continue;

            comp.Stored -= comp.BudCost;
            comp.NextBud = now + comp.BudCooldown;
            // Queued, because the bud joins this query.
            if (MetaData(uid).EntityPrototype is { } proto)
                _helper.QueueSpawn(proto.ID, _transform.GetMapCoordinates(uid));
        }
    }

    private void OnMeleeHit(Entity<FaunaHungerDrainComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner)
            return;

        foreach (var target in args.HitEntities)
        {
            if (!TryComp<HungerComponent>(target, out var hunger))
                continue;

            var drain = MathF.Min(_random.NextFloat(ent.Comp.MinDrain, ent.Comp.MaxDrain), _hunger.GetHunger(hunger));
            if (drain <= 0f)
                continue;

            _hunger.ModifyHunger(target, -drain, hunger);
            ent.Comp.Stored += drain;

            if (_random.Prob(ent.Comp.StaminaChance))
                _stamina.TakeStaminaDamage(target, drain);
        }
    }

    private int CountAlive(EntityUid uid)
    {
        var proto = MetaData(uid).EntityPrototype;
        var count = 0;
        var query = EntityQueryEnumerator<FaunaHungerDrainComponent, MetaDataComponent>();
        while (query.MoveNext(out var other, out _, out var meta))
        {
            if (meta.EntityPrototype == proto && _mobState.IsAlive(other))
                count++;
        }

        return count;
    }
}
