using Content.Server.NPC.Components;
using Content.Shared._WF.Fauna;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Cuts the tentacle cooldown of <see cref="FaunaTentacleCooldownComponent"/> mobs when they are hit, and shows
/// when the tentacle is ready.
/// </summary>
public sealed partial class FaunaTentacleCooldownSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaTentacleCooldownComponent, DamageChangedEvent>(OnDamageChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaTentacleCooldownComponent, NPCUseActionOnTargetComponent>();
        while (query.MoveNext(out var uid, out var comp, out var use))
        {
            var ready = _actions.TryGetActionData(use.ActionEnt, out var action, false)
                && (action.Cooldown is not { } cooldown || cooldown.End <= now)
                && _mobState.IsAlive(uid);

            if (ready == comp.Ready)
                continue;

            comp.Ready = ready;
            _appearance.SetData(uid, FaunaTentacleVisuals.Ready, ready);
        }
    }

    private void OnDamageChanged(Entity<FaunaTentacleCooldownComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased
            || args.Origin == null
            || !TryComp<NPCUseActionOnTargetComponent>(ent, out var use)
            || !_actions.TryGetActionData(use.ActionEnt, out var action, false)
            || action.Cooldown is not { } cooldown)
        {
            return;
        }

        var now = _timing.CurTime;
        if (cooldown.End <= now)
            return;

        var end = cooldown.End - ent.Comp.HitCut;
        _actions.SetCooldown(use.ActionEnt, cooldown.Start, end < now ? now : end);
    }
}
