using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.Wolfmed.Autodoc;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// M3: the surgeon runs the autodoc. A bed patient the pod could help (anyone missing a limb or an organ, and the down,
/// the dead who could still be revived, and the badly hurt when the pod's triage plan has work for them) is taken to the
/// nearest free autodoc on the surgeon's home grid and put in it. He keeps its reservoirs full, puts the limb or organ
/// the patient lacks in its tray (the species' own, from his stock), lets it plan its triage and starts it, and answers
/// it when it asks for a part. When it has nothing more to do he takes the patient out and back to a bed.
/// </summary>
public sealed partial class WGCrewMedicSystem
{
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private Content.Server._WG.Wolfmed.Autodoc.AutodocSystem _autodoc = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    /// <summary>How long a "needs the pod" answer stands before the plan is worked out again.</summary>
    private static readonly TimeSpan OperationCheckGap = TimeSpan.FromSeconds(5);

    /// <summary>How long a patient just out of the pod rests in a bed before the surgeon looks at them for it again.</summary>
    private static readonly TimeSpan OperatedRest = TimeSpan.FromMinutes(2);

    /// <summary>Whether a patient needs the pod, and until when that answer stands.</summary>
    private readonly Dictionary<EntityUid, (TimeSpan Until, bool Needs)> _operationChecks = new();

    /// <summary>True when the patient is the one in this autodoc.</summary>
    public bool InPod(EntityUid pod, EntityUid patient)
    {
        return TryComp<AutodocComponent>(pod, out var comp) && _autodoc.GetOccupant((pod, comp)) == patient;
    }

    /// <summary>
    /// Whether a surgeon should take this bed patient to the autodoc, and to which. Missing a limb or an organ is reason
    /// enough; otherwise the patient must be down, dead but revivable, or badly hurt, and the pod's triage plan must have
    /// something for them. The awake and walking are left to the bed.
    /// </summary>
    public bool NeedsOperation(Entity<WGCrewMedicComponent> medic, EntityUid patient, [NotNullWhen(true)] out EntityUid? pod)
    {
        pod = null;
        if (!Operable(medic, patient) || FreePod(medic, patient) is not { } free || !WouldOperate(medic, free, patient))
            return false;

        pod = free.Owner;
        return true;
    }

    /// <summary>A surgeon's bed patient who isn't past saving.</summary>
    private bool Operable(Entity<WGCrewMedicComponent> medic, EntityUid patient)
    {
        if (medic.Comp.Rank != WGCrewMedicRank.Surgeon || !OnBed(patient) || _container.IsEntityInContainer(patient)
            || !HasComp<BodyComponent>(patient))
            return false;

        return !_mobState.IsDead(patient) || _revival.GetRefusal(patient) is not { } refusal || Fixable.Contains(refusal);
    }

    /// <summary>Whether a pod's library and triage have work for this patient, by the rule above. The answer stands a few seconds.</summary>
    private bool WouldOperate(Entity<WGCrewMedicComponent> medic, Entity<AutodocComponent> pod, EntityUid patient)
    {
        var now = _timing.CurTime;
        if (!_operationChecks.TryGetValue(patient, out var check) || now >= check.Until)
        {
            var needs = Missing(pod, patient, out _) != null
                        || (_mobState.IsDead(patient) || _life.InArrest(patient) || Down(patient) || Serious(medic.Comp, patient))
                        && _autodoc.Plan(pod, patient, preview: true).Count > 0;
            check = (now + OperationCheckGap, needs);
            _operationChecks[patient] = check;
            PruneOperationChecks(now);
        }

        return check.Needs;
    }

    private void PruneOperationChecks(TimeSpan now)
    {
        if (_operationChecks.Count < 128)
            return;

        foreach (var (patient, check) in _operationChecks.ToArray())
        {
            if (now >= check.Until || TerminatingOrDeleted(patient))
                _operationChecks.Remove(patient);
        }
    }

    /// <summary>The nearest powered autodoc on the surgeon's home grid that is empty, idle and nobody else's.</summary>
    private Entity<AutodocComponent>? FreePod(Entity<WGCrewMedicComponent> medic, EntityUid patient)
    {
        if (!TryComp<WGCrewComponent>(medic, out var crew) || _crew.HomeGrid(medic, crew) is not { } home)
            return null;

        var reserved = new HashSet<EntityUid>();
        var others = EntityQueryEnumerator<WGCrewMedicComponent>();
        while (others.MoveNext(out var other, out var otherMedic))
        {
            if (other == medic.Owner)
                continue;
            if (otherMedic.Pod is { } held)
                reserved.Add(held);
            if (otherMedic.Destination is { } heading)
                reserved.Add(heading);
        }

        var from = _transform.GetMapCoordinates(patient);
        Entity<AutodocComponent>? best = null;
        var bestDistance = float.MaxValue;
        var pods = EntityQueryEnumerator<AutodocComponent, TransformComponent>();
        while (pods.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != home || !xform.Anchored || reserved.Contains(uid)
                || comp.State is not (AutodocState.Idle or AutodocState.Complete)
                || _autodoc.GetOccupant((uid, comp)) != null || !_autodoc.IsPowered(uid) || _autodoc.IsEmagged(uid))
                continue;

            var distance = (_transform.GetMapCoordinates(uid, xform).Position - from.Position).Length();
            if (distance >= bestDistance)
                continue;

            best = (uid, comp);
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// The first limb or organ the patient lacks that this pod can put back (a procedure it knows from its triage list),
    /// with the species' own part to use. Brains are left out: a new one would be nobody. Null when nothing is missing.
    /// </summary>
    public EntProtoId? Missing(Entity<AutodocComponent> pod, EntityUid patient, out AutodocRequirement? requirement)
    {
        requirement = null;
        if (!_protos.TryIndex(pod.Comp.Triage, out var triage))
            return null;

        var listed = triage.Steps.SelectMany(step => step.Surgeries).ToHashSet();
        foreach (var entry in _autodoc.GetAvailable(pod, patient))
        {
            if (!entry.Known || !listed.Contains(entry.Surgery))
                continue;

            foreach (var needed in _autodoc.BuildRequirements(pod, entry.Surgery))
            {
                if (needed.Kind is not (AutodocRequirementKind.Part or AutodocRequirementKind.Organ) || !Lacks(patient, needed))
                    continue;

                if (StockFor(patient, needed) is not { } stock)
                    continue;

                requirement = needed;
                return stock;
            }
        }

        return null;
    }

    /// <summary>True when the body really has no part or organ of the kind the requirement names.</summary>
    private bool Lacks(EntityUid patient, AutodocRequirement requirement)
    {
        if (requirement.Kind == AutodocRequirementKind.Organ)
        {
            if (requirement.Component is not { } name || name == "Brain"
                || !EntityManager.ComponentFactory.TryGetRegistration(name, out var registration))
                return false;

            return !_body.GetBodyOrgans(patient).Any(organ => HasComp(organ.Id, registration.Type));
        }

        // A part with no side named ("hands") is a species feature, not something lost.
        return requirement.Symmetry is { } symmetry
               && _body.GetTargetBodyPart(requirement.PartType, symmetry) is { } target
               && _autodoc.ResolvePart(patient, target) == null;
    }

    /// <summary>The patient's species' own part or organ for a requirement, from their body's prototype.</summary>
    private EntProtoId? StockFor(EntityUid patient, AutodocRequirement requirement)
    {
        if (!TryComp<BodyComponent>(patient, out var body) || body.Prototype is not { } bodyId
            || !_protos.TryIndex(bodyId, out var prototype))
            return null;

        foreach (var slot in prototype.Slots.Values)
        {
            if (requirement.Kind == AutodocRequirementKind.Part)
            {
                if (slot.Part is { } partId
                    && _protos.TryIndex(partId, out var partProto)
                    && partProto.TryGetComponent<BodyPartComponent>(out var part, EntityManager.ComponentFactory)
                    && part.PartType == requirement.PartType
                    && (requirement.Symmetry == null || part.Symmetry == requirement.Symmetry))
                    return partId;

                continue;
            }

            if (requirement.Component is not { } name)
                return null;

            foreach (var organId in slot.Organs.Values)
            {
                if (_protos.TryIndex<EntityPrototype>(organId, out var organProto) && organProto.Components.ContainsKey(name))
                    return organId;
            }
        }

        return null;
    }

    /// <summary>Off the bed and to the pod: put in at once when it is in reach, otherwise dragged there.</summary>
    private bool TakeToPod(Entity<WGCrewMedicComponent> medic, EntityUid patient, EntityUid pod)
    {
        if (!TryComp<AutodocComponent>(pod, out var comp))
            return true;

        medic.Comp.Destination = pod;
        Say(medic, "wg-crew-surgeon-take-in");
        _buckle.TryUnbuckle(patient, medic, popup: false);
        if (_interaction.InRangeUnobstructed(medic.Owner, pod, Reach))
            return Deliver(medic, patient, (pod, comp));

        _pulling.TryStartPull(medic, patient);
        return true;
    }

    /// <summary>At the pod: the patient goes in and the first run starts. False while the surgeon stays to attend it.</summary>
    private bool Deliver(Entity<WGCrewMedicComponent> medic, EntityUid patient, Entity<AutodocComponent> pod)
    {
        medic.Comp.Destination = null;
        if (!_autodoc.TryInsert(pod, patient))
        {
            Release(medic);
            return true;
        }

        medic.Comp.Pod = pod;
        if (Begin(medic, pod, patient))
            return false;

        Finish(medic, pod, patient);
        return true;
    }

    /// <summary>One look at the pod: answers it when it waits for a part, starts the next run, or takes the patient out.</summary>
    private bool Attend(Entity<WGCrewMedicComponent> medic, EntityUid podUid, EntityUid patient)
    {
        if (!TryComp<AutodocComponent>(podUid, out var comp))
        {
            Release(medic);
            return true;
        }

        var pod = new Entity<AutodocComponent>(podUid, comp);
        switch (comp.State)
        {
            case AutodocState.Waiting:
                if (comp.Pending is { Kind: AutodocRequirementKind.Item } && comp.BlockedReason == null)
                    _autodoc.Abort(pod); // something for a cavity: not his to supply
                else if (comp.Pending is { Kind: AutodocRequirementKind.Part or AutodocRequirementKind.Organ } pending)
                    StockTray(pod, patient, pending);
                return false;
            case AutodocState.Preparing or AutodocState.Step or AutodocState.Paused:
                return false;
            case AutodocState.Faulted:
                Finish(medic, pod, patient);
                return true;
            default:
                if (Begin(medic, pod, patient))
                    return false;

                Finish(medic, pod, patient);
                return true;
        }
    }

    /// <summary>Stocks the pod, has it plan its triage, and starts it. False when there is nothing more it would do.</summary>
    private bool Begin(Entity<WGCrewMedicComponent> medic, Entity<AutodocComponent> pod, EntityUid patient)
    {
        if (medic.Comp.Operations >= medic.Comp.OperationsPerPatient)
            return false;

        TopUp(medic, pod);
        StockTray(pod, patient, null);
        if (_autodoc.TryPlan(pod, whole: true) == 0 || !_autodoc.TryStart(pod, medic))
            return false;

        medic.Comp.Operations++;
        medic.Comp.Last = WGCrewMedicAction.Surgery;
        Say(medic, "wg-crew-surgeon-operating");
        var ev = new WGCrewTreatedEvent(medic, patient, WGCrewMedicAction.Surgery);
        RaiseLocalEvent(ref ev);
        return true;
    }

    /// <summary>Out of the pod and to a bed (or the morgue, if it came to that). The patient then rests a while.</summary>
    private void Finish(Entity<WGCrewMedicComponent> medic, Entity<AutodocComponent> pod, EntityUid patient)
    {
        _autodoc.TryEject(pod, force: pod.Comp.State == AutodocState.Faulted);
        medic.Comp.Pod = null;
        _operationChecks[patient] = (_timing.CurTime + OperatedRest, false);
        if (InPod(pod, patient))
        {
            Release(medic);
            return;
        }

        Say(medic, "wg-crew-surgeon-done");
        if (PickDestination(medic, patient) is not { } bed)
        {
            Release(medic);
            return;
        }

        medic.Comp.Destination = bed;
        if (_interaction.InRangeUnobstructed(medic.Owner, bed, Reach) && _interaction.InRangeUnobstructed(medic.Owner, patient, Reach))
            Arrive(medic, patient, bed);
        else
            _pulling.TryStartPull(medic, patient);
    }

    /// <summary>
    /// Fills the pod's reservoirs from his stock: an empty slot gets its bottle, unless the last one he put there is
    /// still around somewhere, and his kind of bottle is refilled while it holds nothing but what he puts in it.
    /// </summary>
    public void TopUp(Entity<WGCrewMedicComponent> medic, Entity<AutodocComponent> pod)
    {
        var stock = EnsureComp<WGCrewPodStockComponent>(pod);
        for (var i = 0; i < Math.Min(AutodocComponent.ReservoirSlotIds.Length, medic.Comp.Reservoirs.Count); i++)
        {
            var slot = AutodocComponent.ReservoirSlotIds[i];
            var kind = medic.Comp.Reservoirs[i];
            var item = _slots.GetItemOrNull(pod, slot);
            if (item == null && (!stock.Bottles.TryGetValue(i, out var last) || TerminatingOrDeleted(last)))
            {
                var bottle = Spawn(kind, _transform.GetMapCoordinates(pod));
                if (_slots.TryInsert(pod, slot, bottle, null))
                {
                    item = bottle;
                    stock.Bottles[i] = bottle;
                }
                else
                {
                    QueueDel(bottle);
                }
            }

            if (item is not { } held || MetaData(held).EntityPrototype?.ID != kind.Id || StockReagent(kind) is not { } reagent
                || !_solutions.TryGetDrainableSolution(held, out var solution, out var contents)
                || contents.AvailableVolume <= 0 || contents.Contents.Exists(quantity => !quantity.Reagent.Equals(reagent)))
                continue;

            _solutions.TryAddReagent(solution.Value, reagent, contents.AvailableVolume, out _);
        }
    }

    /// <summary>The one reagent a stock bottle comes filled with; null for anything else.</summary>
    private ReagentId? StockReagent(EntProtoId bottle)
    {
        if (!_protos.Index(bottle).TryGetComponent<SolutionContainerManagerComponent>(out var manager, EntityManager.ComponentFactory)
            || manager.Solutions is not { Count: 1 } solutions)
            return null;

        var contents = solutions.Values.First().Contents;
        return contents.Count == 1 ? contents[0].Reagent : null;
    }

    /// <summary>
    /// Puts the part or organ the patient lacks (or the one the pod is asking for) in the pod's empty tray. Before a run
    /// that is what lets the pod's planner take on the replacement at all.
    /// </summary>
    private void StockTray(Entity<AutodocComponent> pod, EntityUid patient, AutodocRequirement? pending)
    {
        if (_slots.GetItemOrNull(pod, AutodocComponent.TraySlotId) != null)
            return;

        var stock = pending != null ? StockFor(patient, pending) : Missing(pod, patient, out _);
        if (stock is not { } proto)
            return;

        var item = Spawn(proto, _transform.GetMapCoordinates(pod));
        var relock = pod.Comp.State != AutodocState.Waiting;
        _slots.SetLock(pod, AutodocComponent.TraySlotId, false);
        if (!_slots.TryInsert(pod, AutodocComponent.TraySlotId, item, null))
            QueueDel(item);
        if (relock)
            _slots.SetLock(pod, AutodocComponent.TraySlotId, true);
    }
}
