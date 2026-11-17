using Content.Server._WG.Remade.Director;
using Content.Server._WG.Remade.Monolith;
using Content.Shared._WG.Remade;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Tools.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.Remade.Garrison;

/// <summary>
/// Runs the garrison once a second and hears the stone being attacked. Damage to a Monolith, or a tool used on it, calls every
/// Remade of it within forty metres to the attacker. Interaction is heard as a broadcast, since the Gifts hold the stone's own
/// InteractUsing.
/// </summary>
public sealed class WGGarrisonSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WGRemadeDirectorSystem _director = default!;

    private static readonly TimeSpan Rate = TimeSpan.FromSeconds(1);
    private TimeSpan _next;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGMonolithComponent, DamageChangedEvent>(OnDamaged);
        SubscribeLocalEvent<InteractUsingEvent>(OnInteractUsing);
    }

    private void OnDamaged(Entity<WGMonolithComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || ent.Comp.Stage < WGMonolithStage.Growth)
            return;

        var attacker = args.Origin is { } origin && !TerminatingOrDeleted(origin) && HasComp<Robust.Shared.Player.ActorComponent>(origin)
            ? origin
            : _director.NearestPlayer(ent.Owner, WGRemadeDirectorSystem.ConvergeRange);

        if (attacker != null)
            _director.Converge(ent.Owner, attacker.Value, _timing.CurTime);
    }

    private void OnInteractUsing(InteractUsingEvent args)
    {
        if (!TryComp<WGMonolithComponent>(args.Target, out var monolith)
            || monolith.Stage < WGMonolithStage.Growth
            || !HasComp<ToolComponent>(args.Used)
            || _mobState.IsDead(args.User))
        {
            return;
        }

        _director.Converge(args.Target, args.User, _timing.CurTime);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _next)
            return;

        _next = now + Rate;
        var query = EntityQueryEnumerator<WGRemadeDirectorComponent, WGMonolithComponent>();
        while (query.MoveNext(out var uid, out var director, out var monolith))
        {
            if (monolith.Stage >= WGMonolithStage.Growth)
                _director.TickGarrison((uid, director, monolith), now);
        }
    }
}
