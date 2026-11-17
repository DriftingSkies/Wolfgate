using Content.Shared._WG.WolfChem.Narcotics;
using Content.Shared.Bed.Sleep;
using Content.Shared.Drunk;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._WG.WolfChem.Narcotics;

/// <summary>
/// What a high, a crash, a drink or withdrawal looks like from outside, on examine. A face line needs the eyes
/// uncovered and a close look, and shows from a light high; a body line is how they move, which a sealed suit doesn't
/// hide, and shows from a heavy one at any distance. Each is the strongest thing going on, and none names a drug.
/// </summary>
public sealed class WGDrugCuesSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WGAlcoholSystem _alcohol = default!;
    [Dependency] private WGIntoxicationSystem _intoxication = default!;

    /// <summary>Withdrawal shows from this stage; the first is only cravings.</summary>
    public const int WithdrawalStage = 2;

    private static readonly WGDrugFamily[] Families =
    {
        WGDrugFamily.Stimulant, WGDrugFamily.Opioid, WGDrugFamily.Psychedelic, WGDrugFamily.Empathogen,
        WGDrugFamily.Cannabinoid, WGDrugFamily.Combat,
    };

    public override void Initialize()
    {
        base.Initialize();
        // One body can carry all three; the first that is there speaks for it.
        SubscribeLocalEvent<WGIntoxicationComponent, ExaminedEvent>((uid, _, args) => OnExamined(uid, args));
        SubscribeLocalEvent<WGDependenceComponent, ExaminedEvent>((uid, _, args) =>
        {
            if (!HasComp<WGIntoxicationComponent>(uid))
                OnExamined(uid, args);
        });
        SubscribeLocalEvent<DrunkComponent, ExaminedEvent>((uid, _, args) =>
        {
            if (!HasComp<WGIntoxicationComponent>(uid) && !HasComp<WGDependenceComponent>(uid))
                OnExamined(uid, args);
        });
    }

    private void OnExamined(EntityUid uid, ExaminedEvent args)
    {
        var (face, body) = GetCues(uid, args.IsInDetailsRange);
        var target = Identity.Entity(uid, EntityManager);
        if (face != null)
            args.PushMarkup(Loc.GetString(face, ("target", target)));
        if (body != null)
            args.PushMarkup(Loc.GetString(body, ("target", target)));
    }

    /// <summary>The face and body lines a body shows, as Fluent IDs; either may be null.</summary>
    public (string? Face, string? Body) GetCues(EntityUid uid, bool detailed)
    {
        if (_mobState.IsDead(uid))
            return (null, null);

        var face = new Cue();
        var body = new Cue();
        var host = _intoxication.IsNeurowareHost(uid);

        if (TryComp<WGIntoxicationComponent>(uid, out var comp))
        {
            foreach (var family in Families)
            {
                var phase = _intoxication.FamilyPhase(comp, family);
                if (phase == WGDrugPhase.None)
                    continue;

                var name = host ? "synth" : family.ToString().ToLowerInvariant();
                face.Offer($"wg-drug-cue-{name}-face", (int) phase * 10);
                if (phase >= WGDrugPhase.Heavy)
                    body.Offer($"wg-drug-cue-{name}-body-{PhaseName(phase)}", (int) phase * 10);
            }

            if (detailed)
            {
                foreach (var _ in _intoxication.ActiveCrashes(comp))
                {
                    body.Offer(host ? "wg-drug-cue-synth-crash" : "wg-drug-cue-crash", 15);
                    break;
                }
            }
        }

        var drunk = _alcohol.DrunkPhase(uid);
        if (drunk != WGDrugPhase.None)
        {
            face.Offer("wg-drug-cue-drunk-face", (int) drunk * 10 - 1);
            if (drunk >= WGDrugPhase.Heavy)
                body.Offer($"wg-drug-cue-drunk-body-{PhaseName(drunk)}", (int) drunk * 10 - 1);
        }

        if (TryComp<WGDependenceComponent>(uid, out var dependence))
        {
            var stage = 0;
            foreach (var reached in dependence.Stage.Values)
            {
                stage = Math.Max(stage, reached);
            }

            if (stage >= WithdrawalStage)
            {
                body.Offer(host ? "wg-drug-cue-synth-withdrawal" : $"wg-drug-cue-withdrawal-body-{Math.Min(stage, 3)}", stage * 10 + 1);
                if (stage >= 3 && !host)
                    face.Offer("wg-drug-cue-withdrawal-face", stage * 10 + 1);
            }
        }

        // Pupils and skin need a close look at uncovered eyes; someone out cold isn't moving.
        var showFace = detailed && EyesVisible(uid);
        var showBody = !_mobState.IsIncapacitated(uid) && !HasComp<SleepingComponent>(uid);
        return (showFace ? face.Key : null, showBody ? body.Key : null);
    }

    private bool EyesVisible(EntityUid uid)
    {
        var ev = new SeeIdentityAttemptEvent();
        RaiseLocalEvent(uid, ev);
        return (ev.TotalCoverage & IdentityBlockerCoverage.EYES) == 0;
    }

    private static string PhaseName(WGDrugPhase phase)
    {
        return phase == WGDrugPhase.Overdose ? "overdose" : "heavy";
    }

    /// <summary>The strongest line offered so far.</summary>
    private struct Cue
    {
        public string? Key;
        private int _weight;

        public void Offer(string key, int weight)
        {
            if (Key != null && weight <= _weight)
                return;

            Key = key;
            _weight = weight;
        }
    }
}
