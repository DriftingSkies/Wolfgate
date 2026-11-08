using Content.Server.Emoting;
using Content.Shared._WF.Fauna;
using Content.Shared._WF.RMCCore.CameraShake;
using Content.Shared.Drunk;
using Content.Shared.Emoting;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Shakes nearby cameras for <see cref="FaunaShoutComponent"/> and plays the shout's effects on the mob.
/// </summary>
public sealed partial class FaunaShoutSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private RMCCameraShakeSystem _cameraShake = default!;
    [Dependency] private SharedDrunkSystem _drunk = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private AnimatedEmotesSystem _emotes = default!;
    [Dependency] private FaunaEnrageSystem _enrage = default!;

    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaShoutComponent, FaunaShoutActionEvent>(OnShoutAction);
    }

    private void OnShoutAction(Entity<FaunaShoutComponent> ent, ref FaunaShoutActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var filter = Filter.Empty().AddInRange(_transform.GetMapCoordinates(ent), ent.Comp.Range);
        _cameraShake.ShakeCamera(filter, ent.Comp.Shakes, ent.Comp.Strength);
        if (ent.Comp.Popup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner)), ent, PopupType.MediumCaution);

        if (ent.Comp.Effect is { } effect)
            Spawn(effect, Transform(ent).Coordinates);

        if (ent.Comp.Jitter > TimeSpan.Zero)
        {
            _jitter.AddJitter(ent, 8f, 12f);
            ent.Comp.JitterEnd = _timing.CurTime + ent.Comp.Jitter;
        }

        if (ent.Comp.Emote is { } emote)
            _emotes.PlayEmoteAnimation(ent, EnsureComp<AnimatedEmotesComponent>(ent), emote);

        if (ent.Comp.Enrage)
            _enrage.TryEnrage(ent.Owner);

        if (ent.Comp.Disorient <= 0)
            return;

        _mobs.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Range, _mobs);
        foreach (var mob in _mobs)
        {
            if (mob.Owner != ent.Owner)
                _drunk.TryApplyDrunkenness(mob, ent.Comp.Disorient, false);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<FaunaShoutComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.JitterEnd == TimeSpan.Zero || comp.JitterEnd > now)
                continue;

            comp.JitterEnd = TimeSpan.Zero;
            RemComp<JitteringComponent>(uid);
        }
    }
}
