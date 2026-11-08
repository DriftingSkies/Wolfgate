using Content.Server.Bible.Components;
using Content.Server.Body.Components;
using Content.Shared._Common.Consent;
using Content.Shared._NF.Implants.Components;
using Content.Shared._WF.MeridianTraits.Hemophage;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.MeridianTraits.Hemophage;

public sealed partial class WFHemophageSystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private SharedConsentSystem _consent = default!;
    [Dependency] private WFBeingDrainedSystem _held = default!;

    /// <summary>Consent toggle that lets hemophages feed on a player.</summary>
    public static readonly ProtoId<ConsentTogglePrototype> DrainConsent = "WFHemophageDrain";

    private static readonly SoundSpecifier DrinkSound = new SoundPathSpecifier("/Audio/Items/drink.ogg");

    /// <summary>How long past the bite the victim's hold lasts if the bite never reports its end.</summary>
    private static readonly TimeSpan HoldGrace = TimeSpan.FromSeconds(1);

    /// <summary>Feeding rules for a body that has a corrupted tongue but no tumor of its own.</summary>
    private static readonly WFHemophageComponent DrainDefaults = new();

    private void InitializeDrain()
    {
        SubscribeLocalEvent<WFHemophageDrainerComponent, ComponentStartup>(OnDrainerStartup);
        SubscribeLocalEvent<WFHemophageDrainerComponent, ComponentShutdown>(OnDrainerShutdown);
        SubscribeLocalEvent<WFHemophageDrainerComponent, WFHemophageDrainActionEvent>(OnDrainAction);
        SubscribeLocalEvent<WFHemophageDrainerComponent, WFHemophageDrainDoAfterEvent>(OnDrainDoAfter);
    }

    private void OnDrainerStartup(Entity<WFHemophageDrainerComponent> ent, ref ComponentStartup args)
    {
        _actions.AddAction(ent, ref ent.Comp.DrainAction, ent.Comp.DrainActionId);
        if (TryComp<WFHemophageComponent>(ent, out var hemophage) && hemophage.Dormant)
            _actions.SetEnabled(ent.Comp.DrainAction, false);
    }

    private void OnDrainerShutdown(Entity<WFHemophageDrainerComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            _actions.RemoveAction(ent, ent.Comp.DrainAction);
    }

    private void OnDrainAction(Entity<WFHemophageDrainerComponent> ent, ref WFHemophageDrainActionEvent args)
    {
        if (args.Handled || !TryComp<BloodstreamComponent>(ent, out var blood))
            return;

        if (!TryComp<PullerComponent>(ent, out var puller) || puller.Pulling is not { } victim
            || !HasComp<MobStateComponent>(victim))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-no-target"), ent, ent, PopupType.SmallCaution);
            return;
        }

        if (!CanDrain(ent, blood, victim))
            return;

        if (!_consent.HasConsent(victim, DrainConsent))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-no-consent", ("target", victim)), ent, ent, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;
        var biteTime = Rules(ent).BiteTime;
        var doAfter = new DoAfterArgs(EntityManager, ent, biteTime, new WFHemophageDrainDoAfterEvent(), ent, target: victim)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };
        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        _held.Hold(victim, ent, biteTime + HoldGrace);
        _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-held", ("user", ent)), victim, victim, PopupType.MediumCaution);
    }

    private WFHemophageComponent Rules(EntityUid drinker) => CompOrNull<WFHemophageComponent>(drinker) ?? DrainDefaults;

    /// <summary>The most blood this drinker can hold: a hemophage's reserve, or a full bloodstream for anyone else.</summary>
    private FixedPoint2 DrinkerMax(EntityUid drinker, BloodstreamComponent blood)
    {
        return TryComp<WFHemophageComponent>(drinker, out var hemophage)
            ? MaxBlood((drinker, hemophage, blood))
            : blood.BloodMaxVolume;
    }

    /// <summary>Checks the victim is someone whose blood the tongue will take, explaining why not.</summary>
    private bool CanDrain(EntityUid uid, BloodstreamComponent blood, EntityUid victim)
    {
        var rules = Rules(uid);
        if (BloodOf(uid, blood) >= DrinkerMax(uid, blood))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-full"), uid, uid, PopupType.SmallCaution);
            return false;
        }

        if (_mobState.IsDead(victim))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-dead"), uid, uid, PopupType.SmallCaution);
            return false;
        }

        if (!TryComp<BloodstreamComponent>(victim, out var victimBlood)
            || victimBlood.BloodReagent != blood.BloodReagent && !rules.ForeignBloods.Contains(victimBlood.BloodReagent)
            || BloodOf(victim, victimBlood) <= FixedPoint2.Zero)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-unsuitable", ("target", victim)), uid, uid, PopupType.SmallCaution);
            return false;
        }

        if (HasComp<BibleUserComponent>(victim) || HasComp<BibleUserImplantComponent>(victim))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-holy-victim", ("user", uid)), victim, victim, PopupType.MediumCaution);
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-holy", ("target", victim)), uid, uid, PopupType.MediumCaution);
            return false;
        }

        if (HasRepellent(rules, victim, victimBlood))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-garlic-victim", ("user", uid)), victim, victim, PopupType.MediumCaution);
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-garlic", ("target", victim)), uid, uid, PopupType.MediumCaution);
            return false;
        }

        if (IsFoul(rules, victim, blood, victimBlood) && BloodOf(uid, blood) >= blood.BloodMaxVolume * rules.AnimalBloodCapRatio)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-animal-full"), uid, uid, PopupType.SmallCaution);
            return false;
        }

        return true;
    }

    private bool HasRepellent(WFHemophageComponent rules, EntityUid victim, BloodstreamComponent victimBlood)
    {
        return _solution.TryGetSolution(victim, victimBlood.ChemicalSolutionName, out _, out var chemicals)
               && chemicals.GetTotalPrototypeQuantity(rules.RepellentReagent) > FixedPoint2.Zero;
    }

    /// <summary>Anything but a person, monkeys included: its blood is thin, foul and never satisfies.</summary>
    private bool IsAnimal(WFHemophageComponent rules, EntityUid victim)
    {
        return !TryComp<HumanoidAppearanceComponent>(victim, out var humanoid)
               || rules.LesserSpecies.Contains(humanoid.Species);
    }

    /// <summary>Blood the tumor takes but scorns: an animal's, or another species' own kind.</summary>
    private bool IsFoul(WFHemophageComponent rules, EntityUid victim, BloodstreamComponent blood, BloodstreamComponent victimBlood)
    {
        return IsAnimal(rules, victim) || victimBlood.BloodReagent != blood.BloodReagent;
    }

    private void OnDrainDoAfter(Entity<WFHemophageDrainerComponent> ent, ref WFHemophageDrainDoAfterEvent args)
    {
        if (args.Target is { } held)
            _held.Release(held, ent);

        if (args.Cancelled)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-stopped"), ent, ent);
            return;
        }

        if (args.Handled || args.Target is not { } victim)
            return;

        if (!TryComp<PullerComponent>(ent, out var puller) || puller.Pulling != victim)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-stopped"), ent, ent);
            return;
        }

        args.Handled = true;
        TryFeed(ent, victim);
    }

    /// <summary>One bite from the victim: their blood into the drinker, with everything it tells both of them.</summary>
    /// <returns>The blood taken from the victim.</returns>
    public FixedPoint2 TryFeed(EntityUid drinker, EntityUid victim)
    {
        if (!TryComp<BloodstreamComponent>(drinker, out var blood) || !TryComp<BloodstreamComponent>(victim, out var victimBlood)
            || !CanDrain(drinker, blood, victim))
            return FixedPoint2.Zero;

        var rules = Rules(drinker);
        var hemophage = CompOrNull<WFHemophageComponent>(drinker);
        var normal = blood.BloodMaxVolume;
        var animal = IsAnimal(rules, victim);
        var foul = IsFoul(rules, victim, blood, victimBlood);
        var player = !foul && HasComp<ActorComponent>(victim);
        var cap = FixedPoint2.Min(DrinkerMax(drinker, blood), foul ? normal * rules.AnimalBloodCapRatio : DrinkerMax(drinker, blood));
        var bite = normal * rules.BiteRatio;
        var drained = FixedPoint2.Min(BloodOf(victim, victimBlood), FixedPoint2.Min(bite, cap - BloodOf(drinker, blood)));
        if (drained <= FixedPoint2.Zero)
            return FixedPoint2.Zero;

        TakeBlood(victim, victimBlood, drained);
        var gained = drained * (player ? rules.PlayerBloodMultiplier : 1f);
        if (hemophage != null)
            AddBlood((drinker, hemophage, blood), gained, cap);
        else
            _bloodstream.TryModifyBloodLevel(drinker, FixedPoint2.Min(gained, cap - BloodOf(drinker, blood)), blood);

        var victimLeft = BloodOf(victim, victimBlood);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(drinker):player} drained {drained}u of blood from {ToPrettyString(victim):target} (new blood volume: {victimLeft}u)");

        _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-victim", ("user", drinker)), victim, victim, PopupType.MediumCaution);
        var message = animal ? "wf-hemophage-drain-animal"
            : foul ? "wf-hemophage-drain-foreign"
            : player ? "wf-hemophage-drain-player"
            : "wf-hemophage-drain-person";
        _popup.PopupEntity(Loc.GetString(message, ("target", victim)), drinker, drinker);
        _audio.PlayPvs(DrinkSound, drinker, AudioParams.Default.WithVolume(-2f));

        if (drained < bite && BloodOf(drinker, blood) >= DrinkerMax(drinker, blood) - bite)
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-slaked"), drinker, drinker);

        var victimNormal = victimBlood.BloodMaxVolume;
        if (victimLeft <= victimNormal * rules.PaleRatio)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-pale"), drinker, drinker, PopupType.SmallCaution);
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-pale-victim"), victim, victim, PopupType.MediumCaution);
        }

        if (player && hemophage != null)
        {
            hemophage.SatiatedUntil = _timing.CurTime + hemophage.SatiatedDuration;
            _alerts.ShowAlert(drinker, hemophage.SatiatedAlert);
            hemophage.Nausea *= hemophage.PlayerBloodNauseaMultiplier;
        }

        if (foul && hemophage != null)
            Sicken((drinker, hemophage), hemophage.VileNausea);

        var deathLine = victimNormal * rules.DeathRatio;
        if (victimLeft <= deathLine)
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-dry", ("target", victim)), drinker, drinker, PopupType.LargeCaution);
        else if (victimLeft - bite <= deathLine)
            _popup.PopupEntity(Loc.GetString("wf-hemophage-drain-hesitate", ("target", victim)), drinker, drinker, PopupType.MediumCaution);

        return drained;
    }
}
