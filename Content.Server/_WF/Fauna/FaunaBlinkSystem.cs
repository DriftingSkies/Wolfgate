using Content.Shared._WF.Fauna;
using Content.Shared.ActionBlocker;
using Content.Shared.Mobs.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Teleports <see cref="FaunaBlinkComponent"/> mobs around their target, or around themselves when idle or hit.
/// </summary>
public sealed partial class FaunaBlinkSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaBlinkComponent, FaunaBlinkActionEvent>(OnAction);
        SubscribeLocalEvent<FaunaBlinkComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<FaunaBlinkComponent, FaunaShoutActionEvent>(OnShout, after: [typeof(FaunaShoutSystem)]);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaBlinkComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.IdleInterval is not { } interval || now < comp.NextIdle)
                continue;

            comp.NextIdle = now + interval;
            if (!CanBlink(uid) || _helper.TryGetTarget(uid, out _))
                continue;

            TryBlink((uid, comp), uid);
        }
    }

    private void OnAction(Entity<FaunaBlinkComponent> ent, ref FaunaBlinkActionEvent args)
    {
        if (!args.Handled && TryBlink(ent, args.Target))
            args.Handled = true;
    }

    private void OnAttacked(Entity<FaunaBlinkComponent> ent, ref AttackedEvent args)
    {
        if (ent.Comp.HitChance > 0 && CanBlink(ent) && _random.Prob(ent.Comp.HitChance))
            TryBlink(ent, ent);
    }

    private void OnShout(Entity<FaunaBlinkComponent> ent, ref FaunaShoutActionEvent args)
    {
        if (!ent.Comp.AfterShout || !args.Handled || !CanBlink(ent) ||
            !_helper.TryGetTarget(ent, out var target) ||
            TerminatingOrDeleted(target))
            return;

        TryBlink(ent, target);
    }

    private bool CanBlink(EntityUid uid)
    {
        return _mobState.IsAlive(uid) && !_container.IsEntityInContainer(uid) && _actionBlocker.CanMove(uid);
    }

    /// <summary>
    /// Teleports the mob to a free tile <see cref="FaunaBlinkComponent.Distance"/> tiles from <paramref name="centre"/>.
    /// </summary>
    public bool TryBlink(Entity<FaunaBlinkComponent> ent, EntityUid centre)
    {
        var centreXform = Transform(centre);
        var grid = centreXform.ParentUid;
        if (!TryComp<MapGridComponent>(grid, out var gridComp) || Transform(ent).ParentUid != grid)
            return false;

        var middle = _map.TileIndicesFor(grid, gridComp, centreXform.Coordinates);
        if (!_helper.TryPickFreeTile(grid, gridComp, middle, ent.Comp.Distance, FaunaTileShape.Ring, out var landing))
            return false;

        var from = Transform(ent).Coordinates;
        var to = _map.GridTileToLocal(grid, gridComp, landing);

        if (ent.Comp.Effect is { } effect)
        {
            Spawn(effect, from);
            Spawn(effect, to);
        }

        if (ent.Comp.Fade)
            RaiseNetworkEvent(new FaunaBlinkFadeEvent(GetNetEntity(ent), GetNetCoordinates(from)), Filter.Pvs(ent));

        _transform.SetCoordinates(ent, to);
        _audio.PlayPvs(ent.Comp.Sound, ent);
        return true;
    }
}
