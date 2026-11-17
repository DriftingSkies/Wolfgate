using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._WG.WolfChem.Narcotics;
using Content.Server._WG.Synthetics;
using Content.Server._WG.Wolfmed.Hud;
using Content.Server.Chat.Systems;
using Content.Server.Nutrition.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server.Stunnable;
using Content.Server.Temperature.Systems;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared._WG.WolfChem.Neuroware;
using Content.Shared._WG.Wolfmed.Hud;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Power.Components;
using Content.Shared.StatusEffect;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WG.WolfChem.Neuroware;

/// <summary>
/// Neuroware: data sticks load drug programs into a host brain, programs feed the shared intoxication levels at the
/// stick's integrity, damaged builds glitch, and the synthetic HUD shows what is running. A blank takes a copy of
/// another stick or reads a drug in.
/// </summary>
public sealed class WGNeurowareSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private WGNarcoticGoodsSystem _goods = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private StunSystem _stun = default!;
    [Dependency] private SynthBatterySystem _synthBattery = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private WGIntoxicationSystem _intox = default!;

    /// <summary>Integrity kept by a copy.</summary>
    public const float CopyIntegrity = 0.85f;

    /// <summary>Share of its integrity a source keeps each time it is copied.</summary>
    public const float CopyWear = 0.9f;

    /// <summary>Most loads a blank reads in from a drug.</summary>
    public const int EncodeLoads = 3;

    /// <summary>Least level a drug must hold for a blank to read it in.</summary>
    public const float EncodeMinimum = 1f;

    /// <summary>Glitch chance a second at zero integrity.</summary>
    public const float GlitchChance = 0.15f;

    /// <summary>Share of battery capacity a second an overdriving program burns on a synthetic.</summary>
    public const float OverdriveDrain = 0.002f;

    /// <summary>Heat in joules a second an overdriving program adds to a synthetic.</summary>
    public const float OverdriveHeat = 1500f;

    private const string AdviceLine = "wg-neuroware-advice";
    private const string CrashLine = "wg-neuroware-crash-fault";
    private const string HabitLine = "wg-neuroware-habit-loop";
    private const int FalseFaults = 3;
    private static readonly TimeSpan FalseFaultTime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ScrambleTime = TimeSpan.FromSeconds(10);
    private static readonly string[] GlitchEmotes = { "Beep", "Buzz" };

    private TimeSpan _nextTick;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGNeurowareStickComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGNeurowareStickComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WGNeurowareStickComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WGNeurowareStickComponent, WGNeurowareLoadDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<WGNeurowareStickComponent, WGNeurowareEncodeDoAfterEvent>(OnEncodeDoAfter);
        SubscribeLocalEvent<WolfmedSyntheticHudComponent, WolfmedSyntheticHudCollectEvent>(OnCollectFaults);
        SubscribeLocalEvent<BodyComponent, WGChassisRebootedEvent>(OnRebooted);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(1);
        Tick(1f);
    }

    private void OnExamined(Entity<WGNeurowareStickComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.Profile is not { } profile || !_proto.TryIndex(profile, out var proto) || proto.Neuroware == null)
        {
            args.PushMarkup(Loc.GetString("wg-neuroware-stick-blank"));
            return;
        }

        args.PushMarkup(Loc.GetString("wg-neuroware-stick-examine",
            ("program", Loc.GetString(proto.Neuroware.Program)),
            ("integrity", (int) MathF.Round(ent.Comp.Integrity * 100f)),
            ("loads", ent.Comp.Loads)));
    }

    private void OnAfterInteract(Entity<WGNeurowareStickComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (TryComp<WGNeurowareStickComponent>(target, out var blank))
        {
            args.Handled = true;
            TryCopy(ent, (target, blank), args.User);
            return;
        }

        if (!HasComp<BodyComponent>(target))
        {
            if (ent.Comp.Profile == null)
                args.Handled = TryStartEncode(ent, args.User, target);
            return;
        }

        args.Handled = true;
        TryStartLoad(ent, args.User, target);
    }

    private void OnUseInHand(Entity<WGNeurowareStickComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        TryStartLoad(ent, args.User, args.User);
    }

    /// <summary>Checks a stick can load into a target and starts the do-after.</summary>
    public bool TryStartLoad(Entity<WGNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (!CanLoad(stick, user, target))
            return false;

        var self = user == target;
        var args = new DoAfterArgs(EntityManager, user, self ? stick.Comp.SelfDelay : stick.Comp.OtherDelay,
            new WGNeurowareLoadDoAfterEvent(), stick, target: target, used: stick)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(args))
            return false;

        if (!self)
        {
            _popup.PopupEntity(Loc.GetString("wg-neuroware-load-target", ("user", Identity.Entity(user, EntityManager))),
                target, target, PopupType.MediumCaution);
        }

        return true;
    }

    private bool CanLoad(Entity<WGNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (stick.Comp.Profile == null)
        {
            _popup.PopupEntity(Loc.GetString("wg-neuroware-stick-blank"), stick, user);
            return false;
        }

        if (stick.Comp.Loads <= 0)
        {
            _popup.PopupEntity(Loc.GetString("wg-neuroware-stick-spent"), stick, user);
            return false;
        }

        if (!_intox.IsNeurowareHost(target))
        {
            _popup.PopupEntity(Loc.GetString("wg-neuroware-no-port", ("target", Identity.Entity(target, EntityManager))),
                target, user);
            return false;
        }

        return true;
    }

    private void OnLoadDoAfter(Entity<WGNeurowareStickComponent> ent, ref WGNeurowareLoadDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target)
            return;

        args.Handled = Load(ent, args.User, target);
    }

    /// <summary>Starts a stick's program on a host, using one load.</summary>
    public bool Load(Entity<WGNeurowareStickComponent> stick, EntityUid user, EntityUid target)
    {
        if (!CanLoad(stick, user, target)
            || stick.Comp.Profile is not { } profile
            || !_proto.TryIndex(profile, out var proto)
            || proto.Neuroware is not { } twin)
            return false;

        stick.Comp.Loads--;
        var running = EnsureComp<WGNeurowareComponent>(target);
        running.Programs.Add(new WGNeurowareProgram
        {
            Profile = profile,
            Remaining = stick.Comp.Amount ?? twin.Amount,
            Rate = stick.Comp.Rate,
            Integrity = stick.Comp.Integrity,
        });

        if (twin.Banner is { } banner)
            _popup.PopupEntity(Loc.GetString(banner), target, target, PopupType.Medium);

        return true;
    }

    /// <summary>
    /// Copies a program onto a blank stick at <see cref="CopyIntegrity"/> of the source's integrity. The source spends
    /// a load and loses integrity by <see cref="CopyWear"/>, so a stick yields at most as many sticks as it has loads.
    /// </summary>
    public bool TryCopy(Entity<WGNeurowareStickComponent> source, Entity<WGNeurowareStickComponent> blank, EntityUid? user)
    {
        if (source.Comp.Profile == null || blank.Comp.Profile != null)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wg-neuroware-copy-fail"), blank, user.Value);
            return false;
        }

        if (source.Comp.Loads <= 0)
        {
            if (user != null)
                _popup.PopupEntity(Loc.GetString("wg-neuroware-stick-spent"), source, user.Value);
            return false;
        }

        blank.Comp.Profile = source.Comp.Profile;
        blank.Comp.Integrity = source.Comp.Integrity * CopyIntegrity;
        blank.Comp.Amount = source.Comp.Amount;
        blank.Comp.Rate = source.Comp.Rate;
        blank.Comp.Loads = 1;
        source.Comp.Loads--;
        source.Comp.Integrity *= CopyWear;
        _appearance.SetData(blank, WGNeurowareStickVisuals.Copied, true);
        MarkWritten(blank);

        if (user != null)
            _popup.PopupEntity(Loc.GetString("wg-neuroware-copy-done"), blank, user.Value);
        return true;
    }

    /// <summary>Marks a written blank as a narcotic at the market, unless its program's drug is legal.</summary>
    private void MarkWritten(Entity<WGNeurowareStickComponent> blank)
    {
        if (blank.Comp.Profile is { } profile && _proto.TryIndex(profile, out var proto) && proto.Legal)
            return;

        _goods.SetNarcotic(blank, true);
    }

    /// <summary>Starts a blank reading in the drug a thing holds. False when it holds none with a twin.</summary>
    public bool TryStartEncode(Entity<WGNeurowareStickComponent> blank, EntityUid user, EntityUid source)
    {
        if (blank.Comp.Profile != null || !TryFindDrug(source, out _, out _, out _, out _, out var level))
            return false;

        if (level < EncodeMinimum)
        {
            _popup.PopupEntity(Loc.GetString("wg-neuroware-encode-short", ("source", source)), source, user);
            return false;
        }

        var args = new DoAfterArgs(EntityManager, user, blank.Comp.EncodeDelay, new WGNeurowareEncodeDoAfterEvent(),
            blank, target: source, used: blank)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        return _doAfter.TryStartDoAfter(args);
    }

    private void OnEncodeDoAfter(Entity<WGNeurowareStickComponent> ent, ref WGNeurowareEncodeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } source)
            return;

        args.Handled = Encode(ent, source, args.User);
    }

    /// <summary>
    /// Reads a drug out of whatever holds it onto a blank stick, using the drug up. The stick's integrity is the
    /// drug's grade, and it takes up to <see cref="EncodeLoads"/> loads, none larger than the twin's full load.
    /// </summary>
    public bool Encode(Entity<WGNeurowareStickComponent> blank, EntityUid source, EntityUid? user)
    {
        if (blank.Comp.Profile != null
            || !TryFindDrug(source, out var soln, out var reagent, out var profile, out var perUnit, out var level)
            || level < EncodeMinimum
            || !_proto.TryIndex(profile, out var proto)
            || proto.Neuroware is not { } twin)
            return false;

        var taken = Math.Min(level, twin.Amount * EncodeLoads);
        var loads = Math.Clamp((int) MathF.Ceiling(taken / twin.Amount), 1, EncodeLoads);
        var solution = soln.Comp.Solution;

        blank.Comp.Profile = profile;
        blank.Comp.Integrity = Math.Clamp(solution.GetPurity(reagent.Prototype), 0f, 1f);
        blank.Comp.Amount = taken / loads;
        blank.Comp.Loads = loads;
        _appearance.SetData(blank, WGNeurowareStickVisuals.Copied, true);
        MarkWritten(blank);

        if (user != null)
        {
            _popup.PopupEntity(Loc.GetString("wg-neuroware-encode-done", ("source", source),
                ("program", Loc.GetString(twin.Program))), blank, user.Value);
        }

        _solutions.RemoveReagent(soln, reagent, FixedPoint2.New(taken / perUnit));
        // A solid is used up with its drug; a vessel is left empty.
        if (solution.Volume <= FixedPoint2.Zero && HasComp<FoodComponent>(source))
            QueueDel(source);

        return true;
    }

    /// <summary>
    /// Finds the drug with a neuroware twin that a thing holds the most of, by the level it would give in the blood
    /// before its grade.
    /// </summary>
    private bool TryFindDrug(EntityUid source, out Entity<SolutionComponent> soln, out ReagentId reagent,
        out ProtoId<WGDrugProfilePrototype> profile, out float perUnit, out float level)
    {
        soln = default;
        reagent = default;
        profile = default;
        perUnit = 0f;
        level = 0f;
        var found = false;

        foreach (var (_, candidate) in _solutions.EnumerateSolutions(source))
        {
            foreach (var (id, quantity) in candidate.Comp.Solution.Contents)
            {
                foreach (var (dosed, each) in _intox.Doses(id.Prototype))
                {
                    var gives = quantity.Float() * each;
                    if (each <= 0f
                        || found && gives <= level
                        || !_proto.TryIndex(dosed, out var proto)
                        || proto.Neuroware == null)
                        continue;

                    found = true;
                    soln = candidate;
                    reagent = id;
                    profile = dosed;
                    perUnit = each;
                    level = gives;
                }
            }
        }

        return found;
    }

    /// <summary>Runs every program for <paramref name="seconds"/>.</summary>
    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<WGNeurowareComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            for (var i = comp.Programs.Count - 1; i >= 0; i--)
            {
                var program = comp.Programs[i];
                var delta = Math.Min(program.Remaining, program.Rate * seconds);
                program.Remaining -= delta;
                _intox.AddLevel(uid, program.Profile, delta * program.Integrity);
                _intox.AddRush(uid, program.Profile, delta * program.Integrity);

                if (_random.Prob(Math.Clamp((1f - program.Integrity) * GlitchChance * seconds, 0f, 1f)))
                    Glitch((uid, comp));

                if (program.Remaining <= 0f)
                    comp.Programs.RemoveAt(i);
            }

            if (comp.FalseFault != null && _timing.CurTime >= comp.FalseFaultUntil)
                comp.FalseFault = null;

            if (comp.Programs.Count == 0 && comp.FalseFault == null)
                RemCompDeferred<WGNeurowareComponent>(uid);
        }

        var hosts = EntityQueryEnumerator<WGIntoxicationComponent>();
        while (hosts.MoveNext(out var uid, out var intox))
        {
            if (Overdriving(intox) && _intox.IsNeurowareHost(uid))
                Overdrive(uid, seconds);
        }
    }

    private void Glitch(Entity<WGNeurowareComponent> ent)
    {
        switch (_random.Next(4))
        {
            case 0:
                _stun.TryStun(ent, TimeSpan.FromSeconds(1), true);
                break;
            case 1:
                _chat.TryEmoteWithChat(ent, _random.Pick(GlitchEmotes), ignoreActionBlocker: true, forceEmote: true);
                break;
            case 2:
                _status.TryAddStatusEffect(ent, "Stutter", ScrambleTime, true, "ScrambledAccent");
                break;
            default:
                ent.Comp.FalseFault = $"wg-neuroware-false-fault-{_random.Next(1, FalseFaults + 1)}";
                ent.Comp.FalseFaultUntil = _timing.CurTime + FalseFaultTime;
                break;
        }
    }

    /// <summary>True while a combat tier pushes the body past its rated speed.</summary>
    private bool Overdriving(WGIntoxicationComponent intox)
    {
        foreach (var (profile, level) in intox.Levels)
        {
            if (!_proto.TryIndex(profile, out var proto) || proto.Family != WGDrugFamily.Combat)
                continue;

            var tier = proto.TierAt(level);
            if (tier >= 0 && proto.Tiers[tier].Speed > 1f)
                return true;
        }

        return false;
    }

    /// <summary>A synthetic pays for Redline's speed in charge and heat instead of its heart.</summary>
    private void Overdrive(EntityUid body, float seconds)
    {
        if (TryComp<BatteryComponent>(body, out var own))
            _battery.UseCharge(body, own.MaxCharge * OverdriveDrain * seconds, own);
        else if (_powerCell.TryGetBatteryFromSlot(body, out var cell, out var cellBattery))
            _battery.UseCharge(cell.Value, cellBattery.MaxCharge * OverdriveDrain * seconds, cellBattery);
        else if (_synthBattery.TryGetBattery(body, out var synth))
            _battery.UseCharge(synth.Value, synth.Value.Comp.MaxCharge * OverdriveDrain * seconds, synth.Value.Comp);

        _temperature.ChangeHeat(body, OverdriveHeat * seconds, true);
    }

    private void OnCollectFaults(Entity<WolfmedSyntheticHudComponent> ent, ref WolfmedSyntheticHudCollectEvent args)
    {
        if (!_intox.IsNeurowareHost(ent))
            return;

        if (TryComp<WGIntoxicationComponent>(ent, out var intox))
        {
            foreach (var (profile, level) in intox.Levels)
            {
                if (!_proto.TryIndex(profile, out var proto) || proto.Neuroware == null)
                    continue;

                var tier = proto.TierAt(level);
                if (tier < 0)
                    continue;

                var severity = tier == proto.Tiers.Count - 1 ? WolfmedSyntheticSeverity.Crit : WolfmedSyntheticSeverity.Warn;
                foreach (var fault in proto.Neuroware.Faults)
                {
                    Add(args.Faults, fault, severity);
                }
            }

            foreach (var _ in _intox.ActiveCrashes(intox))
            {
                Add(args.Faults, CrashLine, WolfmedSyntheticSeverity.Info);
                break;
            }
        }

        if (TryComp<WGDependenceComponent>(ent, out var dep))
        {
            foreach (var stage in dep.Stage.Values)
            {
                if (stage <= 0)
                    continue;

                Add(args.Faults, HabitLine, stage >= 2 ? WolfmedSyntheticSeverity.Warn : WolfmedSyntheticSeverity.Info);
                break;
            }
        }

        if (CompOrNull<WGNeurowareComponent>(ent)?.FalseFault is { } falseFault)
            Add(args.Faults, falseFault, WolfmedSyntheticSeverity.Crit);
    }

    private static void Add(List<WolfmedSyntheticFault> faults, string line, WolfmedSyntheticSeverity severity)
    {
        foreach (var existing in faults)
        {
            if (existing.Line == line)
                return;
        }

        faults.Add(new WolfmedSyntheticFault(TargetBodyPart.Head, line, severity, AdviceLine));
    }

    private void OnRebooted(Entity<BodyComponent> ent, ref WGChassisRebootedEvent args)
    {
        RemCompDeferred<WGNeurowareComponent>(ent);
        _intox.ClearLevels(ent);
    }
}
