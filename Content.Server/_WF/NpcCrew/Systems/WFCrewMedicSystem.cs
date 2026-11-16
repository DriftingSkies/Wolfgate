using System.Linq;
using Content.Server.Bed.Components;
using Content.Server.Body.Systems;
using Content.Server.Chat.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Server.Medical;
using Content.Server.Medical.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.NPC.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Morgue.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Popups;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Consciousness;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// NPC medics (<see cref="WFCrewMedicComponent"/>). Once a second every free medic picks the patient most in need on
/// his home grid or a ship docked to it: the dead who can be brought back, then hearts in arrest, the unconscious, the
/// downed and bleeding, the downed, those who called for help, and those lying in a bed in Medical. A paramedic takes
/// anyone not yet in Medical; a surgeon works the beds. Each step reads the patient as they are, so another medic's or a
/// player's work is never repeated: restart the heart, tie off a limb's arterial bleed, dress what bleeds, give a
/// stimulant to the unconscious and antitoxin to the poisoned, then patch brute and burn. A paramedic does the
/// lifesaving where the patient lies and drags anyone downed or badly hurt to a free bed in Medical, or the
/// unrevivable dead to the morgue; minor injuries he patches on the spot. Treatments use the real healing items'
/// effects and the Wolfmed revival rules, each behind a do-after the medic must stand still for. A surgeon also runs
/// the autodoc (<c>WFCrewMedicSystem.Surgery.cs</c>), operates by hand when no pod is free
/// (<c>WFCrewMedicSystem.Table.cs</c>) and clones the consenting dead (<c>WFCrewMedicSystem.Cloning.cs</c>).
/// </summary>
public sealed partial class WFCrewMedicSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private DockingSystem _docking = default!;
    [Dependency] private EntityStorageSystem _storage = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private HealingSystem _healing = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PullingSystem _pulling = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedBuckleSystem _buckle = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TourniquetSystem _tourniquet = default!;
    [Dependency] private WFCrewSystem _crew = default!;
    [Dependency] private WFCrewWorkSystem _work = default!;
    [Dependency] private WolfmedCallForHelpSystem _callForHelp = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedRevivalSystem _revival = default!;
    [Dependency] private WolfmedToxinSystem _toxin = default!;
    [Dependency] private WoundBleedingSystem _bleeding = default!;
    [Dependency] private WoundHealingSystem _woundHealing = default!;

    /// <summary>How close a medic stands to treat or take hold of a patient, or to put them to bed.</summary>
    public const float Reach = 1.5f;

    /// <summary>Tries at taking hold of a patient he is taking somewhere before he lets them be.</summary>
    private const int MaxPullTries = 10;

    private static readonly TimeSpan ShockGap = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DoseGap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LineGap = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CprLength = TimeSpan.FromSeconds(10);
    private static readonly SoundSpecifier ShockSound = new SoundPathSpecifier("/Audio/Items/Defib/defib_zap.ogg");

    /// <summary>Refusals a surgeon may yet fix (blood, a heart): such a body goes to a bed, not the morgue.</summary>
    private static readonly HashSet<string> Fixable = new()
    {
        WolfmedRevivalSystem.NoBlood, WolfmedRevivalSystem.NoHeart, WolfmedRevivalSystem.PulsePresent,
        WolfmedRevivalSystem.TooCold,
    };

    /// <summary>How long a patient's care record outlasts the last entry in it; longer than any of its clocks.</summary>
    private static readonly TimeSpan CareMemory = TimeSpan.FromMinutes(3);

    /// <summary>How often care records of patients gone or long left alone are forgotten.</summary>
    private static readonly TimeSpan CarePruneGap = TimeSpan.FromSeconds(30);

    /// <summary>How long a paramedic leaves a patient with nowhere to go before he looks for a bed or drawer again.</summary>
    private static readonly TimeSpan NoRoomWait = TimeSpan.FromSeconds(30);

    /// <summary>How long a medic may go without getting nearer his patient, or their bed, before he gives them up.</summary>
    private static readonly TimeSpan StuckTime = TimeSpan.FromSeconds(30);

    /// <summary>How much nearer counts as getting somewhere.</summary>
    private const float ProgressStep = 0.5f;

    /// <summary>How long a medic leaves be a patient he gave up on for want of a way to them.</summary>
    private static readonly TimeSpan PassOver = TimeSpan.FromSeconds(60);

    /// <summary>The priority of a body a paramedic has nowhere to take: after every living patient.</summary>
    private const int Unplaced = 6;

    /// <summary>Patients taken, and by whom, so two medics don't run for the same one.</summary>
    private readonly Dictionary<EntityUid, EntityUid> _claims = new();

    /// <summary>What the medics have done for each patient lately.</summary>
    private readonly Dictionary<EntityUid, WFCrewPatientCare> _care = new();

    private TimeSpan _nextCarePrune;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFCrewMedicComponent, WFCrewMedicDoAfterEvent>(OnTreated);
        SubscribeLocalEvent<WFCrewMedicComponent, MobStateChangedEvent>(OnMedicState);
        SubscribeLocalEvent<WFCrewMedicComponent, ComponentShutdown>(OnMedicShutdown);
        InitializeCloning();
        InitializeTable();
    }

    private void OnMedicState(Entity<WFCrewMedicComponent> medic, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            Release(medic);
    }

    private void OnMedicShutdown(Entity<WFCrewMedicComponent> medic, ref ComponentShutdown args)
    {
        Release(medic);
        medic.Comp.Passed.Clear();
        foreach (var supply in medic.Comp.Supplies.Values)
            QueueDel(supply);
        medic.Comp.Supplies.Clear();
        if (medic.Comp.Kit is { } kit)
            QueueDel(kit);
        medic.Comp.Kit = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator += frameTime;
        if (_accumulator < 1f)
            return;

        _accumulator = 0f;
        var now = _timing.CurTime;
        PruneCare(now);
        UpdateSelfCare();
        var medics = EntityQueryEnumerator<WFCrewMedicComponent, WFCrewComponent>();
        while (medics.MoveNext(out var uid, out var medic, out var crew))
        {
            if (!_mobState.IsAlive(uid))
                continue;

            if (medic.Rank == WFCrewMedicRank.Surgeon)
                TickCloning((uid, medic), crew);

            if (medic.Patient is { } patient)
            {
                if (!StillCaring((uid, medic), patient))
                    Release((uid, medic));
                else if (Stuck((uid, medic), patient, now))
                    GiveUp((uid, medic), patient, now);
            }

            if (medic.Patient == null && FindPatient((uid, medic), crew) is { } next)
                Assign((uid, medic), next);
        }
    }

    /// <summary>What the medics have done for a patient lately, or null when nothing is on record.</summary>
    public WFCrewPatientCare? CareOf(EntityUid patient)
    {
        return _care.TryGetValue(patient, out var care) ? care : null;
    }

    /// <summary>The patient's care record, made if there is none, kept for <see cref="CareMemory"/> from now.</summary>
    private WFCrewPatientCare Care(EntityUid patient)
    {
        if (!_care.TryGetValue(patient, out var care))
            _care[patient] = care = new WFCrewPatientCare();

        care.Until = _timing.CurTime + CareMemory;
        return care;
    }

    /// <summary>Whether a paramedic leaves this patient where they lie for now, nowhere having been free to take them.</summary>
    private bool NoRoom(EntityUid patient)
    {
        return CareOf(patient) is { } care && _timing.CurTime < care.NoRoomUntil;
    }

    /// <summary>Forgets the care records of patients gone or long left alone.</summary>
    private void PruneCare(TimeSpan now)
    {
        if (now < _nextCarePrune || _care.Count == 0)
            return;

        _nextCarePrune = now + CarePruneGap;
        foreach (var (patient, care) in _care.ToArray())
        {
            if (now >= care.Until || TerminatingOrDeleted(patient))
                _care.Remove(patient);
        }
    }

    /// <summary>
    /// Whether the medic has gone <see cref="StuckTime"/> without getting nearer where he is headed: his patient, or
    /// the bed or drawer he drags them to. Working on them, attending a pod or a table, or standing in reach of the goal
    /// all count as getting there.
    /// </summary>
    private bool Stuck(Entity<WFCrewMedicComponent> medic, EntityUid patient, TimeSpan now)
    {
        var comp = medic.Comp;
        var goal = Drags(medic.Owner, patient) && comp.Destination is { } heading && !TerminatingOrDeleted(heading) ? heading : patient;
        var distance = GoalDistance(medic.Owner, goal);
        if (comp.Busy || comp.Pod != null || comp.Table != null || goal != comp.Goal
            || distance <= comp.GoalDistance - ProgressStep || _interaction.InRangeUnobstructed(medic.Owner, goal, Reach))
        {
            comp.Goal = goal;
            comp.GoalDistance = distance;
            comp.GoalProgress = now;
            return false;
        }

        return now - comp.GoalProgress >= StuckTime;
    }

    /// <summary>How far the medic stands from where he is headed; endlessly far on another map.</summary>
    private float GoalDistance(EntityUid medic, EntityUid goal)
    {
        var from = _transform.GetMapCoordinates(medic);
        var to = _transform.GetMapCoordinates(goal);
        return from.MapId == to.MapId ? (to.Position - from.Position).Length() : float.MaxValue;
    }

    /// <summary>Drops a patient he can't get to, or can't get to their bed with, and leaves them be a while.</summary>
    private void GiveUp(Entity<WFCrewMedicComponent> medic, EntityUid patient, TimeSpan now)
    {
        var passed = medic.Comp.Passed;
        foreach (var (other, until) in passed.ToArray())
        {
            if (now >= until || TerminatingOrDeleted(other))
                passed.Remove(other);
        }

        passed[patient] = now + PassOver;
        Log.Debug($"{ToPrettyString(medic.Owner)} gave up on {ToPrettyString(patient)}: no way to them.");
        Release(medic);
    }

    /// <summary>Whether the medic is leaving this patient be, having given up on reaching them.</summary>
    private bool PassedOver(Entity<WFCrewMedicComponent> medic, EntityUid patient)
    {
        return medic.Comp.Passed.TryGetValue(patient, out var until) && _timing.CurTime < until;
    }

    /// <summary>Gives a medic a patient, taking them from any other medic. What was done for the patient before stands.</summary>
    public void Assign(Entity<WFCrewMedicComponent> medic, EntityUid patient)
    {
        if (_claims.TryGetValue(patient, out var other) && other != medic.Owner && TryComp<WFCrewMedicComponent>(other, out var otherMedic))
            Release((other, otherMedic));

        Release(medic);
        medic.Comp.Patient = patient;
        medic.Comp.Operations = 0;
        _claims[patient] = medic;
    }

    /// <summary>Lets the patient go: drops any treatment under way, lets go of them if dragging, and frees the claim.</summary>
    public void Release(Entity<WFCrewMedicComponent> medic)
    {
        LeaveTable(medic);
        if (medic.Comp.Treatment is { } treatment)
        {
            // Cleared first, so the cancelled do-after's event is seen as one he dropped.
            medic.Comp.Treatment = null;
            if (!TerminatingOrDeleted(medic.Owner) && _doAfter.IsRunning(treatment))
                _doAfter.Cancel(treatment);
        }

        if (medic.Comp.Patient is { } patient)
        {
            if (TryComp<PullableComponent>(patient, out var pullable) && pullable.Puller == medic.Owner)
                _pulling.TryStopPull(patient, pullable, medic);

            if (_claims.TryGetValue(patient, out var owner) && owner == medic.Owner)
                _claims.Remove(patient);
        }

        medic.Comp.Patient = null;
        medic.Comp.Destination = null;
        medic.Comp.Pod = null;
        medic.Comp.Operations = 0;
        medic.Comp.PullTries = 0;
        medic.Comp.Busy = false;
        medic.Comp.Action = WFCrewMedicAction.None;
        medic.Comp.Part = null;
        medic.Comp.Goal = null;
    }

    /// <summary>Where the medic walks next: to the patient, or with the patient in tow to the bed or morgue.</summary>
    public bool Destination(EntityUid mob, out EntityCoordinates coordinates)
    {
        coordinates = default;
        if (!TryComp<WFCrewMedicComponent>(mob, out var medic) || medic.Patient is not { } patient || TerminatingOrDeleted(patient))
            return false;

        var goal = Drags(mob, patient) && medic.Destination is { } bed && !TerminatingOrDeleted(bed) ? bed : patient;
        coordinates = _work.Waypoint(mob, Transform(goal).Coordinates, Reach);
        return true;
    }

    /// <summary>
    /// One look at the patient from where the medic stands, starting the next treatment or the next step of taking them
    /// in. True when he must walk again before he can do more; false while a treatment is under way.
    /// </summary>
    public bool Perform(EntityUid mob)
    {
        if (!TryComp<WFCrewMedicComponent>(mob, out var medic) || medic.Patient is not { } patient)
            return true;

        var ent = (mob, medic);
        if (!StillCaring(ent, patient))
        {
            Release(ent);
            return true;
        }

        if (medic.Busy)
            return false;

        if (medic.Pod is { } pod && InPod(pod, patient))
            return Attend(ent, pod, patient);

        if (medic.Table is { } table)
        {
            if (OnTable(table, patient))
                return AtTable(ent, patient);

            LeaveTable(ent);
        }

        if (Drags(mob, patient))
            return Haul(ent, patient);

        if (!_interaction.InRangeUnobstructed(mob, patient, Reach))
            return true;

        // Heading somewhere with the patient but not pulling them (the pull didn't take): with both in reach they are
        // put there as they are; otherwise he takes hold again, and gives up after a few tries.
        if (medic.Destination is { } heading && !TerminatingOrDeleted(heading) && !_container.IsEntityInContainer(patient))
        {
            if (_interaction.InRangeUnobstructed(mob, heading, Reach))
                return Arrive(ent, patient, heading);

            if (++medic.PullTries > MaxPullTries || !_pulling.TryStartPull(mob, patient))
            {
                if (medic.PullTries > MaxPullTries)
                    Release(ent);
                return true;
            }

            return true;
        }

        var action = NextAction(ent, patient, out var part);
        var urgent = action is WFCrewMedicAction.Shock or WFCrewMedicAction.Tourniquet or WFCrewMedicAction.Dress;
        if (!urgent && NeedsTakingIn(ent, patient))
        {
            if (PickDestination(ent, patient) is { } destination)
            {
                medic.Destination = destination;
                Say(ent, "wf-crew-medic-take-in");
                _pulling.TryStartPull(mob, patient);
                return true;
            }

            // Nowhere free to take them: what can be done here is done, then they wait until a bed or drawer may be free.
            Care(patient).NoRoomUntil = _timing.CurTime + NoRoomWait;
        }

        // Surgery comes before the slower care: only a shock and the bleeding can't wait for the pod.
        if (!urgent && NeedsOperation(ent, patient, out var operatingPod))
            return TakeToPod(ent, patient, operatingPod.Value);

        if (!urgent && NeedsTable(ent, patient, out var operatingTable))
            return TakeToTable(ent, patient, operatingTable.Value);

        if (action == WFCrewMedicAction.None)
        {
            Release(ent);
            return true;
        }

        Start(ent, patient, action, part);
        return false;
    }

    /// <summary>
    /// The next treatment a medic would give this patient, by urgency, with the body part it is for. None when nothing
    /// he can do here would help. The clocks are the patient's, whichever medic last treated them.
    /// </summary>
    public WFCrewMedicAction NextAction(Entity<WFCrewMedicComponent> medic, EntityUid patient, out EntityUid? part)
    {
        part = null;
        var now = _timing.CurTime;
        var care = CareOf(patient);
        var dead = _mobState.IsDead(patient);
        if (dead || _life.InArrest(patient))
        {
            var refusal = _revival.GetRefusal(patient);
            if (refusal == null && now >= (care?.NextShock ?? TimeSpan.Zero)
                && (medic.Comp.Rank == WFCrewMedicRank.Surgeon || (care?.Shocks ?? 0) < medic.Comp.ShocksOnSite || OnBed(patient)))
                return WFCrewMedicAction.Shock;
            if (!dead)
                return WFCrewMedicAction.Cpr;
        }

        foreach (var (id, bodyPart) in _body.GetBodyChildren(patient))
        {
            if (bodyPart.PartType is BodyPartType.Arm or BodyPartType.Leg or BodyPartType.Hand or BodyPartType.Foot
                && _bleeding.HasUndressableBleed(id) && !_bleeding.IsTiedOff(id))
            {
                part = id;
                return WFCrewMedicAction.Tourniquet;
            }
        }

        foreach (var (id, _) in _body.GetBodyChildren(patient))
        {
            if (_bleeding.GetPartRate(id) > 0f && _bleeding.CanDressBleeding(id))
            {
                part = id;
                return WFCrewMedicAction.Dress;
            }
        }

        if (dead)
            return WFCrewMedicAction.None;

        if (Unconscious(patient) && now >= (care?.NextStimulant ?? TimeSpan.Zero))
            return WFCrewMedicAction.Stimulant;

        if (_toxin.GetLoad(patient) > medic.Comp.ToxinLoad && now >= (care?.NextAntitoxin ?? TimeSpan.Zero))
            return WFCrewMedicAction.Antitoxin;

        if (medic.Comp.Rank == WFCrewMedicRank.Paramedic && Serious(medic.Comp, patient))
            return WFCrewMedicAction.None;

        if (NeedsItem(medic, patient, medic.Comp.Brutepack))
            return WFCrewMedicAction.Brute;
        if (NeedsItem(medic, patient, medic.Comp.Suture))
            return WFCrewMedicAction.Suture;
        if (NeedsItem(medic, patient, medic.Comp.Ointment))
            return WFCrewMedicAction.Burn;

        return WFCrewMedicAction.None;
    }

    private void Start(Entity<WFCrewMedicComponent> medic, EntityUid patient, WFCrewMedicAction action, EntityUid? part)
    {
        var delay = TimeSpan.FromSeconds(action switch
        {
            WFCrewMedicAction.Shock => 4,
            WFCrewMedicAction.Cpr => 6,
            WFCrewMedicAction.Stimulant or WFCrewMedicAction.Antitoxin => 2,
            _ => 3,
        });

        var args = new DoAfterArgs(EntityManager, medic, delay, new WFCrewMedicDoAfterEvent(), medic, target: patient)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 2f,
            NeedHand = false,
            BlockDuplicate = true,
        };

        if (!_doAfter.TryStartDoAfter(args, out var id))
            return;

        medic.Comp.Busy = true;
        medic.Comp.Treatment = id;
        medic.Comp.Action = action;
        medic.Comp.Part = part;
        Say(medic, "wf-crew-medic-arrive");
        _popup.PopupEntity(Loc.GetString($"wf-crew-medic-doing-{action.ToString().ToLowerInvariant()}",
            ("medic", medic.Owner), ("patient", patient)), patient, PopupType.Small);
    }

    private void OnTreated(Entity<WFCrewMedicComponent> medic, ref WFCrewMedicDoAfterEvent args)
    {
        // Only the treatment under way is his to finish; a do-after left from one he dropped changes nothing.
        if (medic.Comp.Treatment != args.DoAfter.Id)
            return;

        var action = medic.Comp.Action;
        var part = medic.Comp.Part;
        medic.Comp.Treatment = null;
        medic.Comp.Busy = false;
        medic.Comp.Action = WFCrewMedicAction.None;
        medic.Comp.Part = null;
        if (args.Cancelled || args.Handled || medic.Comp.Patient is not { } patient || args.Target != patient || TerminatingOrDeleted(patient))
            return;

        args.Handled = true;
        if (!Treat(medic, patient, action, part))
            return;

        medic.Comp.Last = action;
        var ev = new WFCrewTreatedEvent(medic, patient, action);
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Gives one treatment. False when it turned out there was nothing to do.</summary>
    private bool Treat(Entity<WFCrewMedicComponent> medic, EntityUid patient, WFCrewMedicAction action, EntityUid? part)
    {
        var now = _timing.CurTime;
        switch (action)
        {
            case WFCrewMedicAction.Shock:
                var shocked = Care(patient);
                shocked.Shocks++;
                shocked.NextShock = now + ShockGap;
                _audio.PlayPvs(ShockSound, patient);
                var revived = _revival.TryDefibrillate(patient, out var line);
                _chat.TrySendInGameICMessage(medic, _revival.LocalizeLine(patient, line), InGameICChatType.Speak, ChatTransmitRange.Normal);
                if (revived)
                {
                    shocked.Shocks = 0;
                    OfferReturn(patient);
                }

                return true;
            case WFCrewMedicAction.Cpr:
                return _revival.StartCpr(patient, CprLength);
            case WFCrewMedicAction.Tourniquet:
                return part is { } limb && _tourniquet.Apply(patient, limb);
            case WFCrewMedicAction.Dress:
                return Heal(medic, patient, part, medic.Comp.Gauze);
            case WFCrewMedicAction.Stimulant:
                Care(patient).NextStimulant = now + DoseGap;
                return Inject(patient, medic.Comp.Stimulant, medic.Comp.Dose);
            case WFCrewMedicAction.Antitoxin:
                Care(patient).NextAntitoxin = now + DoseGap;
                return Inject(patient, medic.Comp.Antitoxin, medic.Comp.Dose);
            case WFCrewMedicAction.Brute:
                return Heal(medic, patient, null, medic.Comp.Brutepack);
            case WFCrewMedicAction.Burn:
                return Heal(medic, patient, null, medic.Comp.Ointment);
            case WFCrewMedicAction.Suture:
                return Heal(medic, patient, null, medic.Comp.Suture);
            case WFCrewMedicAction.TableSurgery:
                return FinishStep(medic, patient);
            default:
                return false;
        }
    }

    /// <summary>The revived player's ghost is asked back into the body, as the defibrillator does.</summary>
    private void OfferReturn(EntityUid patient)
    {
        if (_mind.TryGetMind(patient, out _, out var mind)
            && mind.UserId is { } user
            && _player.TryGetSessionById(user, out var session)
            && mind.CurrentEntity != patient)
            _eui.OpenEui(new ReturnToBodyEui(mind, _mind, _player), session);
    }

    private bool Heal(Entity<WFCrewMedicComponent> medic, EntityUid patient, EntityUid? part, EntProtoId item)
    {
        if (Supply(medic, item) is not { } supply || !TryComp<HealingComponent>(supply, out var healing))
            return false;

        return _woundHealing.TryApplyHealing(patient, part, (supply, healing), medic, out _, out _);
    }

    private bool Inject(EntityUid patient, string reagent, float amount)
    {
        return _bloodstream.TryAddToChemicals(patient, new Solution(reagent, FixedPoint2.New(amount)));
    }

    /// <summary>One of the medic's endless healing items, kept off the map.</summary>
    private EntityUid? Supply(Entity<WFCrewMedicComponent> medic, EntProtoId item)
    {
        if (medic.Comp.Supplies.TryGetValue(item, out var held) && !TerminatingOrDeleted(held))
            return held;

        var spawned = Spawn(item, MapCoordinates.Nullspace);
        medic.Comp.Supplies[item] = spawned;
        return spawned;
    }

    private bool NeedsItem(Entity<WFCrewMedicComponent> medic, EntityUid patient, EntProtoId item)
    {
        return Supply(medic, item) is { } supply
               && TryComp<HealingComponent>(supply, out var healing)
               && TryComp<DamageableComponent>(patient, out var damageable)
               && _healing.IsWoundDamaged((patient, damageable), healing, null);
    }

    /// <summary>A paramedic's patient goes to Medical when downed or badly hurt and not already in a bed or the morgue.</summary>
    public bool NeedsTakingIn(Entity<WFCrewMedicComponent> medic, EntityUid patient)
    {
        return medic.Comp.Rank == WFCrewMedicRank.Paramedic
               && !OnBed(patient)
               && !_container.IsEntityInContainer(patient)
               && (Down(patient) || Serious(medic.Comp, patient));
    }

    /// <summary>
    /// Where to take a patient: the unrevivable dead to a free morgue drawer, everyone else to the nearest free bed
    /// that heals, on the medic's home grid. Null when there is none.
    /// </summary>
    private EntityUid? PickDestination(Entity<WFCrewMedicComponent> medic, EntityUid patient)
    {
        if (!TryComp<WFCrewComponent>(medic, out var crew) || _crew.HomeGrid(medic, crew) is not { } home)
            return null;

        var reserved = new HashSet<EntityUid>();
        var others = EntityQueryEnumerator<WFCrewMedicComponent>();
        while (others.MoveNext(out var other, out var otherMedic))
        {
            if (other != medic.Owner && otherMedic.Destination is { } taken)
                reserved.Add(taken);
        }

        var from = _transform.GetMapCoordinates(patient);
        var morgue = _mobState.IsDead(patient) && _revival.GetRefusal(patient) is { } refusal && !Fixable.Contains(refusal);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        if (morgue)
        {
            var drawers = EntityQueryEnumerator<MorgueComponent, TransformComponent>();
            while (drawers.MoveNext(out var uid, out _, out var xform))
            {
                if (DrawerTakes(uid, patient))
                    Consider(uid, xform);
            }
        }
        else
        {
            var beds = EntityQueryEnumerator<HealOnBuckleComponent, StrapComponent, TransformComponent>();
            while (beds.MoveNext(out var uid, out _, out var strap, out var xform))
            {
                if (strap.BuckledEntities.Count == 0 && !HasComp<OperatingTableComponent>(uid) && CareBed(uid))
                    Consider(uid, xform);
            }
        }

        return best;

        void Consider(EntityUid uid, TransformComponent xform)
        {
            if (xform.GridUid != home || !xform.Anchored || reserved.Contains(uid))
                return;

            var distance = (_transform.GetMapCoordinates(uid, xform).Position - from.Position).Length();
            if (distance >= bestDistance)
                return;

            best = uid;
            bestDistance = distance;
        }
    }

    /// <summary>Whether a morgue drawer is shut and has room for this body.</summary>
    private bool DrawerTakes(EntityUid drawer, EntityUid body)
    {
        return !_storage.IsOpen(drawer) && _storage.CanInsert(body, drawer);
    }

    /// <summary>At the bed or morgue: lets go and puts the patient in it. Done with him, unless the bed is his to work.</summary>
    private bool Haul(Entity<WFCrewMedicComponent> medic, EntityUid patient)
    {
        if (medic.Comp.Destination is not { } destination || TerminatingOrDeleted(destination))
        {
            Release(medic);
            return true;
        }

        if (!_interaction.InRangeUnobstructed(medic.Owner, destination, Reach))
            return true;

        if (TryComp<PullableComponent>(patient, out var pullable))
            _pulling.TryStopPull(patient, pullable, medic);

        return Arrive(medic, patient, destination);
    }

    /// <summary>
    /// With the patient at the bed, morgue drawer or autodoc (or both within his reach): puts them in it. Done with them
    /// then, unless it is the pod he attends.
    /// </summary>
    private bool Arrive(Entity<WFCrewMedicComponent> medic, EntityUid patient, EntityUid destination)
    {
        if (TryComp<AutodocComponent>(destination, out var autodoc))
            return Deliver(medic, patient, (destination, autodoc));

        if (HasComp<OperatingTableComponent>(destination))
        {
            medic.Comp.Destination = null;
            _transform.SetCoordinates(patient, Transform(destination).Coordinates);
            if (_buckle.TryBuckle(patient, medic, destination, popup: false))
            {
                medic.Comp.Table = destination;
                return false;
            }

            Release(medic);
            return true;
        }

        if (HasComp<MorgueComponent>(destination))
        {
            // Someone may have opened or filled the drawer on the way.
            if (!DrawerTakes(destination, patient))
            {
                Release(medic);
                return true;
            }

            _storage.Insert(patient, destination);
        }
        else
        {
            // The patient trails a pace behind and the bed may be out of their reach: they are laid on it.
            _transform.SetCoordinates(patient, Transform(destination).Coordinates);
            _buckle.TryBuckle(patient, medic, destination, popup: false);
        }

        Say(medic, "wf-crew-medic-delivered");
        Release(medic);
        return true;
    }

    /// <summary>Whether a medic should still be with this patient: someone to help, and nobody else has them.</summary>
    private bool StillCaring(Entity<WFCrewMedicComponent> medic, EntityUid patient)
    {
        if (TerminatingOrDeleted(patient) || !_mobState.IsAlive(medic))
            return false;

        if (medic.Comp.Pod is { } pod && InPod(pod, patient) || medic.Comp.Table is { } table && OnTable(table, patient))
            return true;

        if (CompOrNull<PullableComponent>(patient)?.Puller is { } puller && puller != medic.Owner)
            return false;

        // The ships may have undocked, or the patient been barred or turned on the crew, since he took them.
        if (!TryComp<WFCrewComponent>(medic, out var crew) || _crew.HomeGrid(medic, crew) is not { } home
            || Transform(patient).GridUid is not { } grid || !CareGrids(home).Contains(grid) || !MayTreat(medic, patient, home))
            return false;

        if (Drags(medic, patient) || medic.Comp.Busy || medic.Comp.Destination != null)
            return true;

        return !_container.IsEntityInContainer(patient)
               && (NextAction(medic, patient, out _) != WFCrewMedicAction.None || (NeedsTakingIn(medic, patient) && !NoRoom(patient))
                   || NeedsOperation(medic, patient, out _) || NeedsTable(medic, patient, out _));
    }

    /// <summary>The grids a medic works: his home grid and the ships docked to it.</summary>
    private HashSet<EntityUid> CareGrids(EntityUid home)
    {
        var grids = new HashSet<EntityUid> { home };
        foreach (var dock in _docking.GetDocks(home))
        {
            if (dock.Comp.DockedWith is { } other && Transform(other).GridUid is { } grid)
                grids.Add(grid);
        }

        return grids;
    }

    /// <summary>
    /// The patient most in need of this medic, on his home grid or a ship docked to it. Someone waiting for a free bed or
    /// drawer is taken only for what can be done where they lie, and a body in that wait comes after the living.
    /// </summary>
    private EntityUid? FindPatient(Entity<WFCrewMedicComponent> medic, WFCrewComponent crew)
    {
        if (_crew.HomeGrid(medic, crew) is not { } home)
            return null;

        var grids = CareGrids(home);
        var surgeonOnDuty = medic.Comp.Rank == WFCrewMedicRank.Paramedic && SurgeonOnDuty(home);
        var from = _transform.GetMapCoordinates(medic);
        EntityUid? best = null;
        var bestScore = (int.MaxValue, float.MaxValue);
        var bodies = EntityQueryEnumerator<WoundHostComponent, TransformComponent>();
        while (bodies.MoveNext(out var patient, out _, out var xform))
        {
            if (patient == medic.Owner || xform.GridUid is not { } grid || !grids.Contains(grid)
                || _claims.ContainsKey(patient) || PassedOver(medic, patient) || !MayTreat(medic, patient, home))
                continue;

            var onBed = OnBed(patient);
            var priority = Priority(patient, onBed);
            if (priority == null)
                continue;

            // A surgeon works the beds; a paramedic leaves the serious ones there to him.
            if (medic.Comp.Rank == WFCrewMedicRank.Surgeon ? !onBed : onBed && surgeonOnDuty && Serious(medic.Comp, patient))
                continue;

            var noRoom = NoRoom(patient);
            if (NextAction(medic, patient, out _) == WFCrewMedicAction.None && (noRoom || !NeedsTakingIn(medic, patient))
                && !NeedsOperation(medic, patient, out _) && !NeedsTable(medic, patient, out _))
                continue;

            if (noRoom && _mobState.IsDead(patient))
                priority = Unplaced;

            var distance = (_transform.GetMapCoordinates(patient, xform).Position - from.Position).Length();
            var score = (priority.Value, distance);
            if (score.CompareTo(bestScore) >= 0)
                continue;

            best = patient;
            bestScore = score;
        }

        return best;
    }

    /// <summary>
    /// How urgently a body calls for a medic, lowest first, or null when nothing calls one to them: the dead and the
    /// arrested, the unconscious, the downed and bleeding, the downed, a call for help, a bed in Medical.
    /// </summary>
    private int? Priority(EntityUid patient, bool onBed)
    {
        if (_mobState.IsDead(patient) || _life.InArrest(patient))
            return 0;
        if (Unconscious(patient))
            return 1;
        if (Down(patient))
            return _life.GetBleedRate(patient) > 0f ? 2 : 3;
        if (_callForHelp.IsFlagged(patient) || WantsMedic(patient))
            return 4;
        return onBed ? 5 : null;
    }

    private bool MayTreat(Entity<WFCrewMedicComponent> medic, EntityUid patient, EntityUid home)
    {
        if (_container.IsEntityInContainer(patient)
            || CompOrNull<PullableComponent>(patient)?.Puller is { } puller && puller != medic.Owner)
            return false;

        // Nobody who attacked him lately.
        if (TryComp<NPCRetaliationComponent>(medic, out var retaliation))
        {
            var memories = retaliation.AttackMemories;
            if (memories.TryGetValue(patient, out var until) && _timing.CurTime < until)
                return false;
        }

        var ev = new WFCrewMayTreatEvent(medic, patient);
        RaiseLocalEvent(home, ref ev);
        return !ev.Refused;
    }

    private bool SurgeonOnDuty(EntityUid home)
    {
        var medics = EntityQueryEnumerator<WFCrewMedicComponent, WFCrewComponent>();
        while (medics.MoveNext(out var uid, out var medic, out var crew))
        {
            if (medic.Rank == WFCrewMedicRank.Surgeon && _mobState.IsAlive(uid) && _crew.HomeGrid(uid, crew) == home)
                return true;
        }

        return false;
    }

    private bool Drags(EntityUid mob, EntityUid patient)
    {
        return CompOrNull<PullableComponent>(patient)?.Puller == mob;
    }

    /// <summary>Lying in a bed in Medical; on a grid with no medical beds, in any bed that heals.</summary>
    public bool OnBed(EntityUid patient)
    {
        return CompOrNull<BuckleComponent>(patient)?.BuckledTo is { } strap && CareBed(strap);
    }

    /// <summary>
    /// A bed a patient is taken to: a medical bed (<see cref="WFMedicalBedComponent"/>), or any bed that heals on a grid
    /// that has none, so a ship mapped with plain beds still has somewhere.
    /// </summary>
    private bool CareBed(EntityUid bed)
    {
        if (HasComp<WFMedicalBedComponent>(bed))
            return true;

        return HasComp<HealOnBuckleComponent>(bed) && Transform(bed).GridUid is { } grid && !HasMedicalBeds(grid);
    }

    private bool HasMedicalBeds(EntityUid grid)
    {
        var beds = EntityQueryEnumerator<WFMedicalBedComponent, TransformComponent>();
        while (beds.MoveNext(out _, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                return true;
        }

        return false;
    }

    private bool Unconscious(EntityUid patient)
    {
        return _mobState.IsCritical(patient)
               || CompOrNull<WolfmedConsciousnessComponent>(patient)?.State == WolfmedConsciousness.Unconscious;
    }

    private bool Down(EntityUid patient)
    {
        return _mobState.IsIncapacitated(patient)
               || CompOrNull<WolfmedConsciousnessComponent>(patient)?.State is WolfmedConsciousness.Downed or WolfmedConsciousness.Unconscious;
    }

    private static bool Serious(WFCrewMedicComponent medic, EntityUid patient, DamageableComponent? damageable)
    {
        return damageable != null && damageable.TotalDamage > FixedPoint2.New(medic.SeriousDamage);
    }

    private bool Serious(WFCrewMedicComponent medic, EntityUid patient)
    {
        return Serious(medic, patient, CompOrNull<DamageableComponent>(patient));
    }

    private void Say(Entity<WFCrewMedicComponent> medic, string key)
    {
        var now = _timing.CurTime;
        if (now < medic.Comp.NextLine)
            return;

        medic.Comp.NextLine = now + LineGap;
        _chat.TrySendInGameICMessage(medic, Loc.GetString($"{key}-{_random.Next(1, 4)}"), InGameICChatType.Speak, ChatTransmitRange.Normal);
    }
}
