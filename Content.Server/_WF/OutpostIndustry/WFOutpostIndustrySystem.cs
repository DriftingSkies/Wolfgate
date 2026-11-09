using System.Linq;
using Content.Server._WF.OutpostIndustry.Components;
using Content.Server._WF.OutpostRaids;
using Content.Server._WF.Outposts;
using Content.Server._WF.Outposts.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Shared._WF.OutpostIndustry;
using Content.Shared.Examine;
using Content.Shared.Materials;
using Content.Shared.Verbs;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.OutpostIndustry;

/// <summary>
/// An outpost's producers: the parts press, the power exporter and the mining bot dock. Each runs only anchored on an
/// outpost, powered, while someone is home, and while the outpost's producers together are under the hourly cap.
/// </summary>
public sealed partial class WFOutpostIndustrySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private WFOutpostSystem _outposts = default!;
    [Dependency] private WFRaidDirectorSystem _director = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPartsPressComponent, ExaminedEvent>(OnProducerExamined);
        SubscribeLocalEvent<WFPowerExporterComponent, ExaminedEvent>(OnExporterExamined);
        SubscribeLocalEvent<WFPowerExporterComponent, GetVerbsEvent<AlternativeVerb>>(OnExporterVerbs);
        InitializeBots();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextUpdate)
            return;

        var seconds = (float) (now - _nextUpdate + UpdateInterval).TotalSeconds;
        _nextUpdate = now + UpdateInterval;
        UpdatePresses(now);
        UpdateExporters(Math.Min(seconds, 5f));
        UpdateDocks(now);
    }

    /// <summary>Why a producer is idle, as a loc key, or null when it may run.</summary>
    public string? Gate(EntityUid producer, out Entity<WFOutpostComponent, MapGridComponent> outpost)
    {
        outpost = default;
        var xform = Transform(producer);
        if (!xform.Anchored || xform.GridUid is not { } grid || !TryComp<WFOutpostComponent>(grid, out var comp)
            || !TryComp<MapGridComponent>(grid, out var gridComp))
            return "wf-industry-gate-outpost";

        outpost = (grid, comp, gridComp);
        if (!_power.IsPowered(producer))
            return "wf-industry-gate-power";
        if (!_director.SomeoneHome(outpost))
            return "wf-industry-gate-home";
        if (Remaining(outpost) <= 0)
            return "wf-industry-gate-cap";
        return null;
    }

    /// <summary>What the outpost's producers may still make this hour, in spesos.</summary>
    public double Remaining(EntityUid outpost)
    {
        var output = EnsureComp<WFOutpostOutputComponent>(outpost);
        if (_timing.CurTime - output.HourStart >= Hour)
        {
            output.HourStart = _timing.CurTime;
            output.Produced = 0;
        }

        var cap = new WFOutpostProducerCapEvent(_cfg.GetCVar(OutpostIndustryCVars.HourlyCap));
        RaiseLocalEvent(outpost, ref cap);
        return cap.Cap - output.Produced;
    }

    /// <summary>Books value made by an outpost's producers against its hourly cap.</summary>
    public void Record(EntityUid outpost, double value)
    {
        Remaining(outpost);
        Comp<WFOutpostOutputComponent>(outpost).Produced += value;
        var produced = new WFOutpostProducedEvent(value);
        RaiseLocalEvent(outpost, ref produced);
    }

    private void UpdatePresses(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFPartsPressComponent, MaterialStorageComponent>();
        while (query.MoveNext(out var uid, out var press, out var storage))
        {
            if (now < press.NextPress)
                continue;

            press.NextPress = now + press.Interval;
            if (Gate(uid, out var outpost) != null)
                continue;
            if (press.Cost.Any(cost => _materials.GetMaterialAmount(uid, cost.Key, storage) < cost.Value))
                continue;

            foreach (var (material, amount) in press.Cost)
            {
                _materials.TryChangeMaterialAmount(uid, material, -amount, storage);
            }

            var product = Spawn(press.Product, Transform(uid).Coordinates);
            Record(outpost, _pricing.GetPrice(product));
        }
    }

    private void UpdateExporters(float seconds)
    {
        var query = EntityQueryEnumerator<WFPowerExporterComponent>();
        while (query.MoveNext(out var uid, out var exporter))
        {
            if (Gate(uid, out var outpost) != null)
                continue;

            var earned = exporter.Rate * seconds;
            exporter.Credit += earned;
            Record(outpost, earned);
        }
    }

    /// <summary>Pays an exporter's whole spesos of credit out as cash into the user's hands; returns the cash.</summary>
    public EntityUid? CashOut(Entity<WFPowerExporterComponent> exporter, EntityUid user)
    {
        var amount = (int) Math.Floor(exporter.Comp.Credit);
        if (amount <= 0)
            return null;

        exporter.Comp.Credit -= amount;
        var cash = _stack.Spawn(amount, "Credit", Transform(exporter).Coordinates);
        _hands.PickupOrDrop(user, cash);
        _popup.PopupEntity(Loc.GetString("wf-industry-exporter-paid", ("amount", amount)), exporter, user);
        return cash;
    }

    private void OnExporterVerbs(Entity<WFPowerExporterComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Credit < 1)
            return;
        if (Transform(ent).GridUid is not { } grid || !TryComp<WFOutpostComponent>(grid, out var outpost)
            || !_outposts.CanBuild(args.User, (grid, outpost)))
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-industry-exporter-cash-out"),
            Act = () => CashOut(ent, user),
        });
    }

    private void OnExporterExamined(Entity<WFPowerExporterComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-industry-exporter-credit", ("amount", (int) Math.Floor(ent.Comp.Credit))));
        ExamineGate(ent, ref args);
    }

    private void OnProducerExamined(Entity<WFPartsPressComponent> ent, ref ExaminedEvent args)
    {
        var cost = string.Join(", ", ent.Comp.Cost.Select(pair =>
            Loc.GetString("wf-industry-press-cost-entry", ("amount", pair.Value / 100), ("material", Loc.GetString(_proto.Index(pair.Key).Name)))));
        args.PushMarkup(Loc.GetString("wf-industry-press-cost", ("cost", cost)));
        ExamineGate(ent, ref args);
    }

    private void ExamineGate(EntityUid uid, ref ExaminedEvent args)
    {
        if (Gate(uid, out _) is { } reason)
            args.PushMarkup(Loc.GetString(reason));
    }
}

/// <summary>Raised on an outpost when its producers make something, with what it is worth in spesos.</summary>
[ByRefEvent]
public readonly record struct WFOutpostProducedEvent(double Value);

/// <summary>Raised on an outpost for what its producers may make an hour, in spesos.</summary>
[ByRefEvent]
public record struct WFOutpostProducerCapEvent(double Cap);
