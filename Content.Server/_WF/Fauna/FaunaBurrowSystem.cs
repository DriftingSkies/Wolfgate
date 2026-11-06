using Content.Server.Popups;
using Content.Shared._WF.Fauna;
using Content.Shared.Burial.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Throwing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spawns mobs from <see cref="FaunaBurrowComponent"/> holes and lets a shovel fill them in.
/// </summary>
public sealed partial class FaunaBurrowSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaBurrowComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FaunaBurrowComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<FaunaBurrowComponent, FaunaBurrowCloseDoAfterEvent>(OnClose);
    }

    private void OnMapInit(Entity<FaunaBurrowComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextSpawn = _timing.CurTime + ent.Comp.Interval;
    }

    private void OnInteractUsing(Entity<FaunaBurrowComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Closable || !TryComp<ShovelComponent>(args.Used, out var shovel))
            return;

        var doAfter = new DoAfterArgs(EntityManager,
            args.User,
            ent.Comp.CloseDelay / shovel.SpeedModifier,
            new FaunaBurrowCloseDoAfterEvent(),
            ent,
            target: ent,
            used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupEntity(Loc.GetString("fauna-burrow-close-start", ("user", args.User)), ent);

        args.Handled = true;
    }

    private void OnClose(Entity<FaunaBurrowComponent> ent, ref FaunaBurrowCloseDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        _popup.PopupCoordinates(Loc.GetString("fauna-burrow-closed"), Transform(ent).Coordinates);
        QueueDel(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaBurrowComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.NextSpawn > now || comp.Prototypes.Count == 0 || _mobState.IsDead(uid))
                continue;

            comp.NextSpawn = now + comp.Interval;
            comp.Spawned.RemoveAll(mob => TerminatingOrDeleted(mob) || _mobState.IsDead(mob));
            if (comp.Spawned.Count >= comp.MaxAlive)
                continue;

            var index = comp.Index % comp.Prototypes.Count;
            var proto = comp.Prototypes[index];
            comp.Index = (index + 1) % comp.Prototypes.Count;
            var spawned = Spawn(proto, xform.Coordinates);
            comp.Spawned.Add(spawned);
            _audio.PlayPvs(comp.SpawnSound, uid);

            if (comp.ThrowSpeed > 0 && _helper.TryGetTarget(uid, out var target) && !TerminatingOrDeleted(target))
            {
                _throwing.TryThrow(spawned, Transform(target).Coordinates, comp.ThrowSpeed, uid,
                    pushbackRatio: 0, recoil: false);
            }
        }
    }
}
