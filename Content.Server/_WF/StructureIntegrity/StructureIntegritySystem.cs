using Content.Server.Destructible;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Components;

namespace Content.Server._WF.StructureIntegrity;

/// <summary>Shows an anchored structure's remaining integrity on examine, such as 490/500.</summary>
public sealed class StructureIntegritySystem : EntitySystem
{
    [Dependency] private DestructibleSystem _destructible = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DestructibleComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(Entity<DestructibleComponent> ent, ref ExaminedEvent args)
    {
        if (!TryGetIntegrity(ent, out var current, out var max))
            return;

        var fraction = current / (float) max;
        var color = fraction switch
        {
            > 0.66f => "lime",
            > 0.33f => "yellow",
            _ => "red",
        };

        args.PushMarkup(Loc.GetString("wf-structure-integrity-examine",
            ("color", color), ("current", current), ("max", max)), -100);
    }

    /// <summary>Remaining and maximum integrity of an anchored, destructible non-mob, rounded to whole points.</summary>
    public bool TryGetIntegrity(Entity<DestructibleComponent> ent, out int current, out int max)
    {
        current = max = 0;
        if (!Transform(ent).Anchored
            || HasComp<MobStateComponent>(ent)
            || !TryComp<DamageableComponent>(ent, out var damageable))
            return false;

        var destroyedAt = _destructible.DestroyedAt(ent, ent.Comp);
        if (destroyedAt == FixedPoint2.MaxValue || destroyedAt <= FixedPoint2.Zero)
            return false;

        max = destroyedAt.Int();
        current = Math.Clamp((int) MathF.Ceiling((destroyedAt - damageable.TotalDamage).Float()), 0, max);
        return max > 0;
    }
}
