using Content.Server.Atmos.EntitySystems;
using Content.Shared._WG.Modsuits;
using Content.Shared._WG.Modsuits.Components;
using Content.Shared._WG.Modsuits.Modules;
using Content.Shared._WG.Modsuits.Systems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.Modsuits;

/// <summary>Keeps the springlock's activation speed and its water/reagent death trap working.</summary>
public sealed partial class ModSpringlockSystem : EntitySystem
{
    private static readonly TimeSpan GasCheckInterval = TimeSpan.FromSeconds(1);
    private static readonly SoundSpecifier ClickSound = new SoundPathSpecifier("/Audio/Effects/snap.ogg");
    private static readonly SoundSpecifier SnapSound = new SoundPathSpecifier("/Audio/Effects/snap.ogg");
    private static readonly SoundSpecifier SplatSound = new SoundPathSpecifier("/Audio/Effects/Fluids/splat.ogg");

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private ModSuitSystem _modSuit = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    private TimeSpan _nextGasCheck;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSpringlockModuleComponent, ModModuleActivatedEvent>(OnActivated);
        SubscribeLocalEvent<ModSpringlockModuleComponent, ModModuleDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<ModSpringlockModuleComponent, ModSuitWearerChangedEvent>(OnWearerChanged);
        SubscribeLocalEvent<ModSpringlockWearerComponent, ReactionEntityEvent>(OnWearerReaction);
    }

    private void OnActivated(Entity<ModSpringlockModuleComponent> ent, ref ModModuleActivatedEvent args)
    {
        if (ent.Comp.Deadly && args.Wearer is { } wearer)
            EnsureComp<ModSpringlockWearerComponent>(wearer).Module = ent.Owner;
    }

    private void OnDeactivated(Entity<ModSpringlockModuleComponent> ent, ref ModModuleDeactivatedEvent args)
    {
        ResetSnap(ent);

        if (args.Wearer is { } wearer
            && TryComp<ModSpringlockWearerComponent>(wearer, out var armed)
            && armed.Module == ent.Owner)
            RemCompDeferred<ModSpringlockWearerComponent>(wearer);
    }

    private void OnWearerChanged(Entity<ModSpringlockModuleComponent> ent, ref ModSuitWearerChangedEvent args)
    {
        ResetSnap(ent);

        if (args.OldWearer is { } old
            && TryComp<ModSpringlockWearerComponent>(old, out var oldArmed)
            && oldArmed.Module == ent.Owner)
            RemCompDeferred<ModSpringlockWearerComponent>(old);

        if (!ent.Comp.Deadly
            || !TryComp<ModModuleComponent>(ent, out var module)
            || !module.Active)
            return;

        if (args.NewWearer is { } wearer)
            EnsureComp<ModSpringlockWearerComponent>(wearer).Module = ent.Owner;
    }

    private void OnWearerReaction(Entity<ModSpringlockWearerComponent> ent, ref ReactionEntityEvent args)
    {
        if (args.Method != ReactionMethod.Touch
            || ent.Comp.Module is not { } moduleUid
            || !TryComp<ModSpringlockModuleComponent>(moduleUid, out var module)
            || !module.Deadly)
            return;

        SnapSignal((moduleUid, module));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var modules = EntityQueryEnumerator<ModSpringlockModuleComponent>();
        while (modules.MoveNext(out var uid, out var module))
        {
            if (module.SnapAt is { } snapAt && _timing.CurTime >= snapAt)
                SnapShut((uid, module));
        }

        if (_timing.CurTime < _nextGasCheck)
            return;

        _nextGasCheck = _timing.CurTime + GasCheckInterval;

        var wearers = EntityQueryEnumerator<ModSpringlockWearerComponent>();
        while (wearers.MoveNext(out var uid, out var armed))
        {
            if (armed.Module is not { } moduleUid
                || !TryComp<ModSpringlockModuleComponent>(moduleUid, out var module)
                || !module.Deadly)
                continue;

            var air = _atmosphere.GetTileMixture(uid);
            if (air != null && air.GetMoles(Gas.WaterVapor) >= module.WaterVaporThreshold)
                SnapSignal((moduleUid, module));
        }
    }

    private void SnapSignal(Entity<ModSpringlockModuleComponent> ent)
    {
        if (!ent.Comp.Deadly || ent.Comp.SetOff || ent.Comp.SnapAt != null)
            return;

        if (!_modSuit.TryGetModuleSuit(ent, out var suit)
            || suit.Comp.State != ModSuitState.Active
            || suit.Comp.Wearer is not { } wearer
            || _mobState.IsDead(wearer))
            return;

        ent.Comp.SetOff = true;
        var delay = _random.NextFloat(
            (float) ent.Comp.MinSnapDelay.TotalSeconds,
            (float) ent.Comp.MaxSnapDelay.TotalSeconds);
        ent.Comp.SnapAt = _timing.CurTime + TimeSpan.FromSeconds(delay);

        _popup.PopupEntity(Loc.GetString("modsuit-springlock-click", ("module", ent.Owner)), wearer, wearer, PopupType.MediumCaution);
        _audio.PlayPvs(ClickSound, wearer);
    }

    private static void ResetSnap(Entity<ModSpringlockModuleComponent> ent)
    {
        ent.Comp.SetOff = false;
        ent.Comp.SnapAt = null;
    }

    private void SnapShut(Entity<ModSpringlockModuleComponent> ent)
    {
        ent.Comp.SnapAt = null;
        ent.Comp.SetOff = false;

        if (!_modSuit.TryGetModuleSuit(ent, out var suit)
            || suit.Comp.Wearer is not { } wearer
            || TerminatingOrDeleted(wearer))
            return;

        _popup.PopupEntity(Loc.GetString("modsuit-springlock-snap", ("module", ent.Owner)), wearer, wearer, PopupType.LargeCaution);
        _audio.PlayPvs(SnapSound, wearer);
        _audio.PlayPvs(SplatSound, wearer);
        _damageable.TryChangeDamage(wearer, ent.Comp.SnapDamage, ignoreResistances: true, origin: ent.Owner);
    }
}
