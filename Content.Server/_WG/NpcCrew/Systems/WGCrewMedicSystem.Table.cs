using System.Diagnostics.CodeAnalysis;
using Content.Server._Shitmed.Medical.Surgery;
using Content.Server._WG.NpcCrew.Components;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.Wolfmed.Autodoc;
using Content.Shared.Body.Part;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Popups;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>
/// The surgeon at the operating table, when every pod on his home grid is busy or there is none: the same patients the
/// pod would take, on the nearest free table. He works the pod's own triage one procedure at a time, each step a
/// do-after with the real Shitmed step behind it, from an off-map pod that holds his tools, tray and anaesthetic. He
/// takes off what is in the way, whole; a shock, and bleeding anywhere he hasn't opened, come first. When there is
/// nothing more to do he takes the patient back to a bed.
/// </summary>
public sealed partial class WGCrewMedicSystem
{
    [Dependency] private SurgerySystem _surgery = default!;

    /// <summary>Passes of a step that change nothing before he gives the procedure up.</summary>
    private const int MaxTableStalls = 3;

    /// <summary>Looks at the next step in one go, so a run of things that no longer apply can't hang the tick.</summary>
    private const int MaxTableLooks = 32;

    private void InitializeTable()
    {
        SubscribeLocalEvent<WGCrewMedicComponent, WolfmedSurgeryToolsEvent>(OnTableTools);
    }

    /// <summary>At a table his tools are his instruments' tools and tray.</summary>
    private void OnTableTools(Entity<WGCrewMedicComponent> medic, ref WolfmedSurgeryToolsEvent args)
    {
        if (medic.Comp.Table != null && medic.Comp.Kit is { } kit)
            RaiseLocalEvent(kit, ref args);
    }

    /// <summary>Whether a surgeon should operate on this bed patient at a table, and at which: only with no pod free.</summary>
    public bool NeedsTable(Entity<WGCrewMedicComponent> medic, EntityUid patient, [NotNullWhen(true)] out EntityUid? table)
    {
        table = null;
        if (!Operable(medic, patient) || FreePod(medic, patient) != null
            || FreeTable(medic, patient) is not { } free || Instruments(medic) is not { } kit
            || !WouldOperate(medic, kit, patient))
            return false;

        table = free;
        return true;
    }

    /// <summary>The nearest anchored operating table on the surgeon's home grid that is empty or already holds the patient.</summary>
    private EntityUid? FreeTable(Entity<WGCrewMedicComponent> medic, EntityUid patient)
    {
        if (!TryComp<WGCrewComponent>(medic, out var crew) || _crew.HomeGrid(medic, crew) is not { } home)
            return null;

        var reserved = new HashSet<EntityUid>();
        var others = EntityQueryEnumerator<WGCrewMedicComponent>();
        while (others.MoveNext(out var other, out var otherMedic))
        {
            if (other == medic.Owner)
                continue;
            if (otherMedic.Table is { } held)
                reserved.Add(held);
            if (otherMedic.Destination is { } heading)
                reserved.Add(heading);
        }

        var from = _transform.GetMapCoordinates(patient);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        var tables = EntityQueryEnumerator<OperatingTableComponent, StrapComponent, TransformComponent>();
        while (tables.MoveNext(out var uid, out _, out var strap, out var xform))
        {
            if (xform.GridUid != home || !xform.Anchored || reserved.Contains(uid)
                || strap.BuckledEntities.Count > 0 && !OnTable(uid, patient))
                continue;

            var distance = (_transform.GetMapCoordinates(uid, xform).Position - from.Position).Length();
            if (distance >= bestDistance)
                continue;

            best = uid;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>His instruments, made the first time he needs them.</summary>
    private Entity<AutodocComponent>? Instruments(Entity<WGCrewMedicComponent> medic)
    {
        if (medic.Comp.Kit is not { } kit || TerminatingOrDeleted(kit))
        {
            kit = EntityManager.Spawn(medic.Comp.Instruments, doMapInit: true);
            medic.Comp.Kit = kit;
        }

        return TryComp<AutodocComponent>(kit, out var comp) ? (kit, comp) : null;
    }

    private bool OnTable(EntityUid table, EntityUid patient)
    {
        return CompOrNull<BuckleComponent>(patient)?.BuckledTo == table;
    }

    /// <summary>To the table: already there, laid on it when it is in reach, otherwise dragged there.</summary>
    private bool TakeToTable(Entity<WGCrewMedicComponent> medic, EntityUid patient, EntityUid table)
    {
        if (OnTable(table, patient))
        {
            medic.Comp.Table = table;
            return false;
        }

        medic.Comp.Destination = table;
        Say(medic, "wg-crew-surgeon-table");
        _buckle.TryUnbuckle(patient, medic, popup: false);
        if (_interaction.InRangeUnobstructed(medic.Owner, table, Reach))
            return Arrive(medic, patient, table);

        _pulling.TryStartPull(medic, patient);
        return true;
    }

    /// <summary>One look at the table: the next step, or the patient back to a bed. False while he stays to work.</summary>
    private bool AtTable(Entity<WGCrewMedicComponent> medic, EntityUid patient)
    {
        if (!_interaction.InRangeUnobstructed(medic.Owner, patient, Reach))
            return true;

        if (NextAction(medic, patient, out _) == WGCrewMedicAction.Shock)
        {
            Start(medic, patient, WGCrewMedicAction.Shock, null);
            return false;
        }

        if (BleedOutsideTheCut(patient, out var bleeding) is { } dressing)
        {
            Start(medic, patient, dressing, bleeding);
            return false;
        }

        if (Instruments(medic) is not { } kit)
        {
            FinishTable(medic, patient);
            return true;
        }

        // An incision or a cautery's burn lands a moment behind its step, and it is his own work, not new work.
        if (medic.Comp.Operations > 0)
            _autodoc.MarkHandWounds(kit, patient);

        if (medic.Comp.Procedure == null && _timing.CurTime < medic.Comp.TableSettle)
            return false;

        if (NextStep(medic, kit, patient) is not { } step)
        {
            FinishTable(medic, patient);
            return true;
        }

        StartStep(medic, patient, step);
        return false;
    }

    /// <summary>A tourniquet or dressing needed on a part he hasn't opened; his own incisions are the operation's to close.</summary>
    private WGCrewMedicAction? BleedOutsideTheCut(EntityUid patient, out EntityUid? part)
    {
        part = null;
        foreach (var (id, bodyPart) in _body.GetBodyChildren(patient))
        {
            if (HasComp<IncisionOpenComponent>(id))
                continue;

            if (bodyPart.PartType is BodyPartType.Arm or BodyPartType.Leg or BodyPartType.Hand or BodyPartType.Foot
                && _bleeding.HasUndressableBleed(id) && !_bleeding.IsTiedOff(id))
            {
                part = id;
                return WGCrewMedicAction.Tourniquet;
            }
        }

        foreach (var (id, _) in _body.GetBodyChildren(patient))
        {
            if (!HasComp<IncisionOpenComponent>(id) && _bleeding.GetPartRate(id) > 0f && _bleeding.CanDressBleeding(id))
            {
                part = id;
                return WGCrewMedicAction.Dress;
            }
        }

        return null;
    }

    /// <summary>The next step of the procedure in hand, starting the next procedure when that one is done. Null when no work is left.</summary>
    private WGCrewTableStep? NextStep(Entity<WGCrewMedicComponent> medic, Entity<AutodocComponent> kit, EntityUid patient)
    {
        var undressed = false;
        var stocked = false;
        for (var look = 0; look < MaxTableLooks; look++)
        {
            if (medic.Comp.Procedure is not { } entry)
            {
                if (!PickProcedure(medic, kit, patient))
                    return null;
                continue;
            }

            if (_autodoc.ResolvePart(patient, entry.Part) is not { } part
                || _surgery.GetSingleton(entry.Surgery) is not { } surgery
                || _surgery.GetNextStep(patient, part, surgery) is not { } next
                || MetaData(next.Surgery.Owner).EntityPrototype?.ID is not { } owning)
            {
                medic.Comp.Procedure = null;
                continue;
            }

            var stepId = next.Surgery.Comp.Steps[next.Step];
            if (_surgery.GetSingleton(stepId) is not { } stepEnt)
            {
                GiveUp(medic, kit, entry);
                continue;
            }

            if (_surgery.WolfmedCanPerformStep(medic, patient, part, stepEnt, out var reason))
                return new WGCrewTableStep(part, owning, stepId, _surgery.WolfmedStepDuration(stepEnt));

            if (reason == StepInvalidReason.Armor && !undressed && TryComp<BodyPartComponent>(part, out var bodyPart)
                && _autodoc.TakeOffWhole(medic, patient, bodyPart.PartType))
            {
                undressed = true;
                continue;
            }

            if (reason == StepInvalidReason.MissingTool && !stocked)
            {
                stocked = true;
                StockTray(kit, patient, null);
                continue;
            }

            GiveUp(medic, kit, entry);
        }

        return null;
    }

    /// <summary>Takes the first thing on the pod's triage for this patient. False at the cap, or when nothing is left.</summary>
    private bool PickProcedure(Entity<WGCrewMedicComponent> medic, Entity<AutodocComponent> kit, EntityUid patient)
    {
        if (medic.Comp.Operations >= medic.Comp.TableProcedures)
            return false;

        if (medic.Comp.Operations == 0)
            _autodoc.SnapshotHandWounds(kit, patient);

        TopUp(medic, kit);
        StockTray(kit, patient, null);
        var plan = _autodoc.Plan(kit, patient, preview: true);
        if (plan.Count == 0)
            return false;

        medic.Comp.Procedure = plan[0];
        medic.Comp.Stalls = 0;
        if (medic.Comp.Operations++ == 0)
        {
            medic.Comp.Last = WGCrewMedicAction.TableSurgery;
            Say(medic, "wg-crew-surgeon-cutting");
            var ev = new WGCrewTreatedEvent(medic, patient, WGCrewMedicAction.TableSurgery);
            RaiseLocalEvent(ref ev);
        }

        _autodoc.AnaesthetiseFor(kit, patient, plan[0].Surgery);
        return true;
    }

    private void GiveUp(Entity<WGCrewMedicComponent> medic, Entity<AutodocComponent> kit, AutodocProcedureEntry entry)
    {
        kit.Comp.FailedProcedures.Add((entry.Surgery.Id, entry.Part));
        medic.Comp.Procedure = null;
    }

    private void StartStep(Entity<WGCrewMedicComponent> medic, EntityUid patient, WGCrewTableStep step)
    {
        var seconds = MathF.Max(0.1f, step.Seconds * medic.Comp.TableSpeed);
        var args = new DoAfterArgs(EntityManager, medic, TimeSpan.FromSeconds(seconds), new WGCrewMedicDoAfterEvent(), medic, target: patient)
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
        medic.Comp.Action = WGCrewMedicAction.TableSurgery;
        medic.Comp.Step = step;
        _popup.PopupEntity(Loc.GetString("wg-crew-surgeon-table-step", ("medic", medic.Owner), ("patient", patient), ("part", step.Part)),
            patient, PopupType.Small);
    }

    /// <summary>
    /// A step's do-after finished: performs it through Shitmed, then ends the procedure at its closing step, when it
    /// stopped applying, or when the step keeps changing nothing. False, so it isn't logged as a treatment each time.
    /// </summary>
    private bool FinishStep(Entity<WGCrewMedicComponent> medic, EntityUid patient)
    {
        if (medic.Comp.Step is not { } step || medic.Comp.Procedure is not { } entry || Instruments(medic) is not { } kit)
            return false;

        medic.Comp.Step = null;
        medic.Comp.TableSettle = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0f, kit.Comp.PodWoundGrace));
        var before = _autodoc.PartSignature(step.Part);
        if (!_surgery.WolfmedPerformStep(medic, patient, step.Part, step.Surgery, step.Step, byHand: true))
        {
            GiveUp(medic, kit, entry);
            return false;
        }

        var complete = _surgery.WolfmedIsStepComplete(patient, step.Part, step.Step, step.Surgery);
        if (complete && step.Surgery == entry.Surgery && _autodoc.IsFinalStep(entry.Surgery, step.Step))
        {
            medic.Comp.Procedure = null;
            return false;
        }

        medic.Comp.Stalls = complete || _autodoc.PartSignature(step.Part) != before ? 0 : medic.Comp.Stalls + 1;
        if (medic.Comp.Stalls >= MaxTableStalls)
            GiveUp(medic, kit, entry);

        return false;
    }

    /// <summary>Done at the table: the patient rests, and goes back to a bed like one out of the pod.</summary>
    private void FinishTable(Entity<WGCrewMedicComponent> medic, EntityUid patient)
    {
        LeaveTable(medic);
        _operationChecks[patient] = (_timing.CurTime + OperatedRest, false);
        Say(medic, "wg-crew-surgeon-table-done");
        if (PickDestination(medic, patient) is not { } bed)
        {
            Release(medic);
            return;
        }

        medic.Comp.Destination = bed;
        _buckle.TryUnbuckle(patient, medic, popup: false);
        if (_interaction.InRangeUnobstructed(medic.Owner, bed, Reach) && _interaction.InRangeUnobstructed(medic.Owner, patient, Reach))
            Arrive(medic, patient, bed);
        else
            _pulling.TryStartPull(medic, patient);
    }

    /// <summary>Stops work at the table: the patient wakes, and his instruments are cleared for the next one.</summary>
    private void LeaveTable(Entity<WGCrewMedicComponent> medic)
    {
        if (medic.Comp.Table == null)
            return;

        medic.Comp.Table = null;
        medic.Comp.Procedure = null;
        medic.Comp.Step = null;
        medic.Comp.Stalls = 0;
        if (medic.Comp.Kit is not { } uid || !TryComp<AutodocComponent>(uid, out var comp))
            return;

        var kit = new Entity<AutodocComponent>(uid, comp);
        if (medic.Comp.Patient is { } patient)
            _autodoc.WakeOccupant(kit, patient);

        kit.Comp.AnaestheticGiven = false;
        kit.Comp.FailedProcedures.Clear();
        kit.Comp.PreProcedureWounds.Clear();
        if (_slots.GetItemOrNull(kit, AutodocComponent.TraySlotId) is { } leftover)
            QueueDel(leftover);
    }
}
