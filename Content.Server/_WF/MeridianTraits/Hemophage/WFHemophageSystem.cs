using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Body.Components;
using Content.Server.Chat.Managers;
using Content.Server.Popups;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Body.Systems;
using Content.Server.Storage.Components;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.MeridianTraits.Hemophage;
using Content.Shared.Actions;
using Content.Shared.Alert;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Body.Prototypes;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.MeridianTraits.Hemophage;

/// <summary>
/// Runs a hemophage: the trait's heart swap, the tumor's steady blood use, the reserve past a full bloodstream, blood
/// drunk or injected going straight into it, and death when it runs dry. Abilities, diet and feeding are in the
/// partial files.
/// </summary>
public sealed partial class WFHemophageSystem : SharedWFHemophageSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private WFTumorCorruptionSystem _corruption = default!;
    [Dependency] private WolfmedLifeSystem _wolfmedLife = default!;

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    /// <summary>Chance per second of the flavour convulsion while critically low on blood.</summary>
    private const float ConvulseChance = 0.075f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFHemophageTumorComponent, OrganAddedToBodyEvent>(OnTumorAdded);
        SubscribeLocalEvent<WFHemophageTumorComponent, OrganRemovedFromBodyEvent>(OnTumorRemoved);
        SubscribeLocalEvent<WFHemophageComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WFHemophageComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WFHemophageComponent, WFHemophageBloodAlertEvent>(OnBloodAlertClicked);
        SubscribeLocalEvent<WFHemophageComponent, WFMailGoodiesEvent>(OnMailGoodies);
        InitializeAbilities();
        InitializeDiet();
        InitializeDrain();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateTraits();
        UpdatePendingIngestion();

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFHemophageComponent, BloodstreamComponent>();
        while (query.MoveNext(out var uid, out var comp, out var blood))
        {
            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + UpdateInterval;
            Tick((uid, comp, blood), (float) UpdateInterval.TotalSeconds);
        }
    }

    private void Tick(Entity<WFHemophageComponent, BloodstreamComponent> ent, float seconds)
    {
        var (uid, comp, blood) = ent;
        if (_mobState.IsDead(uid))
            return;

        FixedPoint2 normalVolume = blood.BloodMaxVolume;
        var normal = normalVolume.Float();
        var now = _timing.CurTime;

        if (comp.SatiatedUntil is { } satiated && now >= satiated)
        {
            comp.SatiatedUntil = null;
            _alerts.ClearAlert(uid, comp.SatiatedAlert);
        }

        AbsorbBlood(ent);
        TickAbilities(ent, seconds);
        TickClots(ent);
        TickBloodWarmth(ent, seconds);
        TickNausea(ent, seconds);

        if (!comp.MasterOfTheHouse && !InCloset(uid))
            RemoveBlood(ent, DrainRate(comp) * normal * seconds);

        TopUp(ent);

        var ratio = TotalBlood(ent).Float() / normal;
        if (ratio <= comp.DeathRatio)
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-ran-out"), uid, uid, PopupType.LargeCaution);
            _adminLog.Add(LogType.Damaged, LogImpact.High, $"{ToPrettyString(uid):player} starved to death from lack of blood caused by their tumor.");
            _wolfmedLife.Kill(uid);
            return;
        }

        if (ratio < comp.PowerMinRatio && _random.Prob(ConvulseChance * seconds))
        {
            _popup.PopupEntity(Loc.GetString("wf-hemophage-convulse", ("target", uid)), uid,
                Filter.PvsExcept(uid), true, PopupType.MediumCaution);
        }

        UpdateBloodAlert(ent);
    }

    /// <summary>Blood the tumor uses per second, as a fraction of a normal bloodstream.</summary>
    private float DrainRate(WFHemophageComponent comp)
    {
        var rate = comp.DrainPerSecond;
        if (comp.Dormant)
            rate /= comp.DormantDrainDivisor;
        if (comp.SatiatedUntil != null)
            rate *= comp.SatiatedDrainMultiplier;
        return rate;
    }

    /// <summary>A closet hides a hemophage from the tumor's hunger; a body bag doesn't.</summary>
    private bool InCloset(EntityUid uid)
    {
        return _container.TryGetContainingContainer((uid, null, null), out var container)
               && HasComp<EntityStorageComponent>(container.Owner)
               && !_tag.HasTag(container.Owner, "BodyBag");
    }

    #region Blood

    /// <summary>Bloodstream and reserve together.</summary>
    public FixedPoint2 TotalBlood(Entity<WFHemophageComponent, BloodstreamComponent> ent)
    {
        return StreamVolume(ent.Owner, ent.Comp2) + ent.Comp1.Reserve;
    }

    /// <summary>The most blood this hemophage can hold.</summary>
    public FixedPoint2 MaxBlood(Entity<WFHemophageComponent, BloodstreamComponent> ent)
    {
        return ent.Comp2.BloodMaxVolume * ent.Comp1.MaxRatio;
    }

    private FixedPoint2 StreamVolume(EntityUid uid, BloodstreamComponent blood)
    {
        return _solution.TryGetSolution(uid, blood.BloodSolutionName, out _, out var solution)
            ? solution.Volume
            : FixedPoint2.Zero;
    }

    /// <summary>Adds blood up to <paramref name="cap"/> in total, filling the bloodstream before the reserve.</summary>
    /// <returns>The blood actually added.</returns>
    public FixedPoint2 AddBlood(Entity<WFHemophageComponent, BloodstreamComponent> ent, FixedPoint2 amount, FixedPoint2? cap = null)
    {
        var (uid, comp, blood) = ent;
        var limit = FixedPoint2.Min(cap ?? MaxBlood(ent), MaxBlood(ent));
        amount = FixedPoint2.Min(amount, limit - TotalBlood(ent));
        if (amount <= FixedPoint2.Zero)
            return FixedPoint2.Zero;

        var room = blood.BloodMaxVolume - StreamVolume(uid, blood);
        var toStream = FixedPoint2.Max(FixedPoint2.Zero, FixedPoint2.Min(amount, room));
        if (toStream > FixedPoint2.Zero)
            _bloodstream.TryModifyBloodLevel(uid, toStream, blood);

        comp.Reserve += amount - toStream;
        return amount;
    }

    /// <summary>Takes blood out without spilling it, emptying the reserve before the bloodstream.</summary>
    public void RemoveBlood(Entity<WFHemophageComponent, BloodstreamComponent> ent, FixedPoint2 amount)
    {
        var (uid, comp, blood) = ent;
        if (amount <= FixedPoint2.Zero)
            return;

        var fromReserve = FixedPoint2.Min(amount, comp.Reserve);
        comp.Reserve -= fromReserve;
        amount -= fromReserve;
        if (amount <= FixedPoint2.Zero)
            return;

        if (_solution.TryGetSolution(uid, blood.BloodSolutionName, out var stream, out _))
            _solution.SplitSolution(stream.Value, amount);
    }

    /// <summary>Takes blood out of any body without spilling it. A hemophage loses its reserve first.</summary>
    public FixedPoint2 TakeBlood(EntityUid uid, BloodstreamComponent blood, FixedPoint2 amount)
    {
        if (TryComp<WFHemophageComponent>(uid, out var hemophage))
        {
            var ent = (uid, hemophage, blood);
            amount = FixedPoint2.Min(amount, TotalBlood(ent));
            RemoveBlood(ent, amount);
            return amount;
        }

        if (!_solution.TryGetSolution(uid, blood.BloodSolutionName, out var stream, out var solution))
            return FixedPoint2.Zero;

        amount = FixedPoint2.Min(amount, solution.Volume);
        _solution.SplitSolution(stream.Value, amount);
        return amount;
    }

    /// <summary>Room left for blood in a hemophage, reserve included; null for anyone else.</summary>
    public FixedPoint2? BloodRoom(EntityUid uid)
    {
        if (!TryComp<WFHemophageComponent>(uid, out var hemophage) || !TryComp<BloodstreamComponent>(uid, out var blood))
            return null;

        var full = (uid, hemophage, blood);
        return FixedPoint2.Max(FixedPoint2.Zero, MaxBlood(full) - TotalBlood(full));
    }

    /// <summary>Gives blood to a hemophage, past a full bloodstream into the reserve. False for anyone else.</summary>
    public bool TryGiveBlood(EntityUid uid, FixedPoint2 amount)
    {
        if (amount <= FixedPoint2.Zero || !TryComp<WFHemophageComponent>(uid, out var hemophage)
            || !TryComp<BloodstreamComponent>(uid, out var blood))
            return false;

        AddBlood((uid, hemophage, blood), amount);
        return true;
    }

    /// <summary>Any body's blood, a hemophage's reserve included.</summary>
    public FixedPoint2 BloodOf(EntityUid uid, BloodstreamComponent blood)
    {
        return TryComp<WFHemophageComponent>(uid, out var hemophage)
            ? TotalBlood((uid, hemophage, blood))
            : StreamVolume(uid, blood);
    }

    /// <summary>Moves reserve into the bloodstream as it drains, so the two read as one pool.</summary>
    private void TopUp(Entity<WFHemophageComponent, BloodstreamComponent> ent)
    {
        var (uid, comp, blood) = ent;
        if (comp.Reserve <= FixedPoint2.Zero)
            return;

        var room = blood.BloodMaxVolume - StreamVolume(uid, blood);
        var moved = FixedPoint2.Min(room, comp.Reserve);
        if (moved <= FixedPoint2.Zero)
            return;

        if (_bloodstream.TryModifyBloodLevel(uid, moved, blood))
            comp.Reserve -= moved;
    }

    /// <summary>Blood that reaches the stomach or the veins as a reagent becomes the hemophage's own.</summary>
    private void AbsorbBlood(Entity<WFHemophageComponent, BloodstreamComponent> ent)
    {
        var (uid, _, blood) = ent;
        var absorbed = FixedPoint2.Zero;

        if (TryComp<BodyComponent>(uid, out var body))
        {
            foreach (var stomach in _body.GetBodyOrganEntityComps<StomachComponent>((uid, body)))
            {
                if (_solution.TryGetSolution(stomach.Owner, StomachSystem.DefaultSolutionName, out var contents, out _))
                    absorbed += TakeReagent(contents.Value, blood.BloodReagent);
            }
        }

        if (_solution.TryGetSolution(uid, blood.ChemicalSolutionName, out var chemicals, out _))
            absorbed += TakeReagent(chemicals.Value, blood.BloodReagent);

        if (absorbed <= FixedPoint2.Zero)
            return;

        AddBlood(ent, absorbed);
        ent.Comp1.BloodWarmthUntil = _timing.CurTime + TimeSpan.FromSeconds(absorbed.Float());
    }

    private FixedPoint2 TakeReagent(Entity<Content.Shared.Chemistry.Components.SolutionComponent> solution, string reagent)
    {
        var taken = FixedPoint2.Zero;
        foreach (var quantity in solution.Comp.Solution.Contents.ToArray())
        {
            if (quantity.Reagent.Prototype != reagent)
                continue;

            _solution.RemoveReagent(solution, quantity);
            taken += quantity.Quantity;
        }

        return taken;
    }

    private void UpdateBloodAlert(Entity<WFHemophageComponent, BloodstreamComponent> ent)
    {
        var max = MaxBlood(ent).Float();
        var severity = max > 0f ? (short) Math.Clamp(MathF.Round(TotalBlood(ent).Float() / max * 10f), 0, 10) : (short) 0;
        _alerts.ShowAlert(ent.Owner, ent.Comp1.BloodAlert, severity);
    }

    private void OnBloodAlertClicked(Entity<WFHemophageComponent> ent, ref WFHemophageBloodAlertEvent args)
    {
        if (args.Handled || !TryComp<BloodstreamComponent>(ent, out var blood))
            return;

        args.Handled = true;
        var full = (ent.Owner, ent.Comp, blood);
        _popup.PopupEntity(Loc.GetString("wf-hemophage-blood-level",
                ("current", (int) TotalBlood(full).Float()),
                ("max", (int) MaxBlood(full).Float())),
            ent, ent);
    }

    #endregion

    private void OnMailGoodies(Entity<WFHemophageComponent> ent, ref WFMailGoodiesEvent args)
    {
        foreach (var (parcel, weight) in ent.Comp.MailGoodies)
            args.Parcels.TryAdd(parcel, weight);
    }

    #region Lifecycle

    /// <summary>Swaps the heart for a tumor a tick after the trait arrives, once the body has its organs.</summary>
    private void UpdateTraits()
    {
        var query = EntityQueryEnumerator<WFHemophageTraitComponent, BodyComponent>();
        while (query.MoveNext(out var uid, out var trait, out var body))
        {
            if (trait.Applied)
                continue;

            trait.Applied = true;
            ApplyTrait((uid, trait, body));
        }
    }

    private void ApplyTrait(Entity<WFHemophageTraitComponent, BodyComponent> ent)
    {
        var (uid, trait, body) = ent;
        if (!TryComp<BloodstreamComponent>(uid, out var blood) || blood.BloodReagent != trait.RequiredBlood)
        {
            Log.Warning($"{ToPrettyString(uid)} has no {trait.RequiredBlood} for a hemophage tumor; trait skipped.");
            return;
        }

        if (_body.GetBodyOrganEntityComps<WFHemophageTumorComponent>((uid, body)).Count == 0 && !SwapHeart(ent))
            return;

        if (!_body.GetBodyOrgans(uid, body).Any(organ => organ.Component.SlotId == WFTumorCorruptionSystem.TongueSlot))
            InsertTongue(ent);

        foreach (var organ in _body.GetBodyOrgans(uid, body).ToList())
        {
            if (TryComp<WFHemophageTumorComponent>(organ.Id, out _))
                continue;

            if (organ.Component.SlotId is "liver" or "lungs" or "stomach" or WFTumorCorruptionSystem.TongueSlot)
                _corruption.Corrupt(organ.Id, instant: true);
        }

        if (TryComp<WFHemophageComponent>(uid, out var hemophage))
        {
            var full = (uid, hemophage, blood);
            hemophage.Reserve = FixedPoint2.Zero;
            AddBlood(full, blood.BloodMaxVolume * hemophage.StartRatio - TotalBlood(full));
            UpdateBloodAlert(full);
        }

        if (TryComp<ActorComponent>(uid, out var actor))
            _chat.DispatchServerMessage(actor.PlayerSession, Loc.GetString("wf-hemophage-spawn-text"));
    }

    /// <summary>Gives the head the tongue Meridian's hemophages drink with; Wolfgate bodies grow none of their own.</summary>
    private void InsertTongue(Entity<WFHemophageTraitComponent, BodyComponent> ent)
    {
        var (uid, trait, body) = ent;
        if (_body.GetBodyChildrenOfType(uid, BodyPartType.Head, body).FirstOrDefault() is not { Id.Valid: true } head
            || !_body.TryCreateOrganSlot(head.Id, WFTumorCorruptionSystem.TongueSlot, out _, head.Component))
            return;

        var tongue = Spawn(trait.Tongue, Transform(uid).Coordinates);
        if (!_body.InsertOrgan(head.Id, tongue, WFTumorCorruptionSystem.TongueSlot, head.Component))
            Del(tongue);
    }

    private bool SwapHeart(Entity<WFHemophageTraitComponent, BodyComponent> ent)
    {
        var (uid, trait, body) = ent;
        var hearts = _body.GetBodyOrganEntityComps<HeartComponent>((uid, body));
        BodyPartComponent? partComp = null;
        EntityUid part = default;
        if (hearts.Count > 0 && _container.TryGetContainingContainer((hearts[0].Owner, null, null), out var container))
        {
            part = container.Owner;
            TryComp(part, out partComp);
        }

        if (partComp == null)
        {
            Log.Warning($"{ToPrettyString(uid)} has no heart for a hemophage tumor; trait skipped.");
            return false;
        }

        var old = hearts[0].Owner;
        var slot = hearts[0].Comp2.SlotId;
        var metabolizers = CompOrNull<MetabolizerComponent>(old)?.MetabolizerTypes is { } oldTypes ? new HashSet<ProtoId<MetabolizerTypePrototype>>(oldTypes) : null;
        _body.WfDeleteOrgan(uid, old);

        var tumor = Spawn(trait.Tumor, Transform(uid).Coordinates);
        if (metabolizers != null && CompOrNull<MetabolizerComponent>(tumor)?.MetabolizerTypes is { } tumorTypes)
        {
            tumorTypes.Clear();
            tumorTypes.UnionWith(metabolizers);
        }

        if (_body.InsertOrgan(part, tumor, slot, partComp))
            return true;

        Log.Error($"Could not insert a hemophage tumor into {ToPrettyString(uid)}.");
        Del(tumor);
        return false;
    }

    private void OnTumorAdded(Entity<WFHemophageTumorComponent> ent, ref OrganAddedToBodyEvent args)
    {
        EnsureComp<WFHemophageComponent>(args.Body);
    }

    private void OnTumorRemoved(Entity<WFHemophageTumorComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (!TerminatingOrDeleted(args.OldBody))
            RemCompDeferred<WFHemophageComponent>(args.OldBody);
    }

    private void OnStartup(Entity<WFHemophageComponent> ent, ref ComponentStartup args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;

        comp.HadBreathingImmunity = HasComp<BreathingImmunityComponent>(uid);
        EnsureComp<BreathingImmunityComponent>(uid);
        comp.RemovedHunger = RemComp<HungerComponent>(uid);
        comp.RemovedThirst = RemComp<ThirstComponent>(uid);

        _actions.AddAction(uid, ref comp.DormantAction, comp.DormantActionId);
        _actions.AddAction(uid, ref comp.RegenAction, comp.RegenActionId);
        _actions.AddAction(uid, ref comp.ClotAction, comp.ClotActionId);
        _actions.AddAction(uid, ref comp.MasterAction, comp.MasterActionId);

        comp.NextUpdate = _timing.CurTime + UpdateInterval;
    }

    private void OnShutdown(Entity<WFHemophageComponent> ent, ref ComponentShutdown args)
    {
        var uid = ent.Owner;
        var comp = ent.Comp;
        if (TerminatingOrDeleted(uid))
            return;

        RemoveBreathingImmunity(ent);
        if (comp.RemovedHunger)
            RestoreFromPrototype<HungerComponent>(uid);
        if (comp.RemovedThirst)
            RestoreFromPrototype<ThirstComponent>(uid);

        RestoreStamina(ent);

        _actions.RemoveAction(uid, comp.DormantAction);
        _actions.RemoveAction(uid, comp.RegenAction);
        _actions.RemoveAction(uid, comp.ClotAction);
        _actions.RemoveAction(uid, comp.MasterAction);

        _alerts.ClearAlert(uid, comp.BloodAlert);
        _alerts.ClearAlert(uid, comp.SatiatedAlert);
        _alerts.ClearAlert(uid, comp.RegenAlert);
        _alerts.ClearAlert(uid, comp.MasterAlert);
        _alerts.ClearAlert(uid, comp.SlaveAlert);

        comp.Dormant = false;
        comp.MasterOfTheHouse = false;
        Movement.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>Takes away the tumor's breathing immunity, unless the body had its own.</summary>
    private void RemoveBreathingImmunity(Entity<WFHemophageComponent> ent)
    {
        if (!ent.Comp.HadBreathingImmunity)
            RemComp<BreathingImmunityComponent>(ent);
    }

    /// <summary>Puts back a component the tumor took away, as the body's own prototype defines it.</summary>
    private void RestoreFromPrototype<T>(EntityUid uid) where T : IComponent, new()
    {
        if (HasComp<T>(uid))
            return;

        var name = Factory.GetComponentName<T>();
        if (MetaData(uid).EntityPrototype is { } proto && proto.Components.TryGetValue(name, out var entry))
        {
            EntityManager.AddComponents(uid, new ComponentRegistry { [name] = entry }, false);
            return;
        }

        AddComp<T>(uid);
    }

    #endregion
}
