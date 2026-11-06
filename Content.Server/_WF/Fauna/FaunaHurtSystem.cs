using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Reacts to damage on <see cref="FaunaHurtComponent"/> mobs.
/// </summary>
public sealed partial class FaunaHurtSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaHurtComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<FaunaHurtComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || _timing.CurTime < ent.Comp.NextHurt || !_mobState.IsAlive(ent))
            return;

        ent.Comp.NextHurt = _timing.CurTime + ent.Comp.Cooldown;
        _audio.PlayPvs(ent.Comp.Sound, ent);
        // The spawn waits for the next tick, since damage can land inside another system's component loop.
        if (ent.Comp.Spawn is { } spawn)
            _helper.QueueSpawn(spawn, _transform.GetMapCoordinates(ent));
    }
}
