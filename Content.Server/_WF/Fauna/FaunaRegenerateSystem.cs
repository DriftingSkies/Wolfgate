using Content.Server.Popups;
using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Heals <see cref="FaunaRegenerateComponent"/> mobs once a second while their regenerate runs.
/// </summary>
public sealed partial class FaunaRegenerateSystem : EntitySystem
{
    private static readonly TimeSpan TickTime = TimeSpan.FromSeconds(1);

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaRegenerateComponent, FaunaRegenerateActionEvent>(OnRegenerateAction);
    }

    private void OnRegenerateAction(Entity<FaunaRegenerateComponent> ent, ref FaunaRegenerateActionEvent args)
    {
        if (args.Handled || ent.Comp.EndAt > _timing.CurTime)
            return;

        args.Handled = true;
        ent.Comp.EndAt = _timing.CurTime + ent.Comp.Duration;
        ent.Comp.NextTick = _timing.CurTime + TickTime;
        _popup.PopupEntity(Loc.GetString("fauna-regenerate-start", ("user", ent.Owner)), ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaRegenerateComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.EndAt == TimeSpan.Zero || comp.NextTick > now)
                continue;

            if (comp.NextTick > comp.EndAt || !_mobState.IsAlive(uid))
            {
                comp.EndAt = TimeSpan.Zero;
                continue;
            }

            comp.NextTick += TickTime;
            var share = (float) (TickTime / comp.Duration);
            _damageable.TryChangeDamage(uid, comp.Heal * share, true, false);
        }
    }
}
