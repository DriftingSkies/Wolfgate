using Content.Server.NPC.HTN;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Sends <see cref="FaunaLeashComponent"/> mobs home when they lose their target.
/// </summary>
public sealed partial class FaunaLeashSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaLeashComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<FaunaLeashComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.Home = Transform(ent).Coordinates;
        ent.Comp.LastTarget = _timing.CurTime;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaLeashComponent, HTNComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var htn, out var xform))
        {
            if (now < comp.NextCheck)
                continue;

            comp.NextCheck = now + TimeSpan.FromSeconds(1);
            if (!_mobState.IsAlive(uid) || comp.Home is not { } home)
                continue;

            if (htn.Blackboard.TryGetValue<EntityUid>(FaunaHelperSystem.TargetKey, out _, EntityManager))
            {
                comp.LastTarget = now;
                continue;
            }

            if (now < comp.LastTarget + comp.Timeout
                || xform.Coordinates.TryDistance(EntityManager, home, out var distance) && distance <= comp.Range)
                continue;

            comp.LastTarget = now;
            if (comp.Popup is { } popup)
                _popup.PopupCoordinates(Loc.GetString(popup, ("user", uid)), xform.Coordinates, PopupType.Medium);

            _transform.SetCoordinates(uid, home);
            Heal((uid, comp));
        }
    }

    private void Heal(Entity<FaunaLeashComponent> ent)
    {
        if (ent.Comp.HealFraction <= 0 || !TryComp<DamageableComponent>(ent, out var damageable))
            return;

        var total = damageable.TotalDamage.Float();
        if (total <= 0)
            return;

        var fraction = Math.Min(1f, Math.Max(ent.Comp.HealFraction, ent.Comp.MinHeal / total));
        _damageable.TryChangeDamage(ent.Owner, -damageable.Damage * fraction, true, false, damageable);
    }
}
