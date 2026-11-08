using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Rolls the on-hit procs of <see cref="FaunaMeleeProcComponent"/> and kills its spawns when the mob dies.
/// </summary>
public sealed partial class FaunaMeleeProcSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedEmpSystem _emp = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaMeleeProcComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<FaunaMeleeProcComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<FaunaMeleeProcComponent, EntityTerminatingEvent>(OnTerminating);
    }

    private void OnMeleeHit(Entity<FaunaMeleeProcComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.User != ent.Owner)
            return;

        if (ent.Comp.BloodDrainMax > 0)
        {
            foreach (var hit in args.HitEntities)
            {
                _bloodstream.TryModifyBloodLevel(hit,
                    -_random.NextFloat(ent.Comp.BloodDrainMin, ent.Comp.BloodDrainMax));
            }
        }

        if (args.HitEntities.Count == 0 || !_random.Prob(ent.Comp.Chance))
            return;

        var target = args.HitEntities[0];
        if (ent.Comp.Spawn is { } proto)
        {
            ent.Comp.Spawned.RemoveAll(spawned => TerminatingOrDeleted(spawned) || _mobState.IsDead(spawned));
            if (ent.Comp.MaxSpawned <= 0 || ent.Comp.Spawned.Count < ent.Comp.MaxSpawned)
            {
                ent.Comp.Spawned.Add(Spawn(proto, Transform(ent).Coordinates));
                if (ent.Comp.SpawnSpeech is { } speech)
                {
                    _chat.TrySendInGameICMessage(ent, Loc.GetString(speech), InGameICChatType.Speak, hideChat: true,
                        checkRadioPrefix: false);
                }
            }
        }

        if (ent.Comp.Damage != null)
            _damageable.TryChangeDamage(target, ent.Comp.Damage, origin: ent);

        if (ent.Comp.Solution is { } solution &&
            _solution.TryGetSolution(ent.Owner, solution, out var venom) &&
            HasComp<BloodstreamComponent>(target))
        {
            _bloodstream.TryAddToChemicals(target, _solution.SplitSolution(venom.Value, ent.Comp.TransferAmount));
        }

        if (ent.Comp.Bleed > 0)
            _bloodstream.TryModifyBleedAmount(target, ent.Comp.Bleed);

        if (ent.Comp.EmpRange > 0)
            _emp.EmpPulse(Transform(target).Coordinates, ent.Comp.EmpRange, ent.Comp.EmpEnergy, ent.Comp.EmpDuration, ent);

        _audio.PlayPvs(ent.Comp.Sound, ent);
        if (ent.Comp.Popup is { } popup)
            _popup.PopupEntity(Loc.GetString(popup, ("user", ent.Owner), ("target", target)), target, PopupType.SmallCaution);
    }

    private void OnMobStateChanged(Entity<FaunaMeleeProcComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            KillSpawned(ent);
    }

    private void OnTerminating(Entity<FaunaMeleeProcComponent> ent, ref EntityTerminatingEvent args)
    {
        KillSpawned(ent);
    }

    private void KillSpawned(Entity<FaunaMeleeProcComponent> ent)
    {
        foreach (var spawned in ent.Comp.Spawned)
        {
            if (TerminatingOrDeleted(spawned))
                continue;

            if (ent.Comp.GibSpawned)
                _helper.QueueGib(spawned);
            else if (TryComp<MobStateComponent>(spawned, out var mob))
                _mobState.ChangeMobState(spawned, MobState.Dead, mob);
        }

        ent.Comp.Spawned.Clear();
    }
}
