using Content.Server.Administration.Logs;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Popups;
using Content.Shared._Shitmed.Body.Components;
using Content.Shared._WF.Wolfmed.Medical;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.DragDrop;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// The ventilator: a tank in the stand, the mask fitted to a patient in reach over a do-after, and a breath taken
/// from the tank on the respirator's cycle. Each breath with gas in it keeps the patient ventilated until the next.
/// </summary>
public sealed partial class WolfmedVentilatorSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private GasTankSystem _gasTank = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WolfmedVentilatorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WolfmedVentilatorComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<WolfmedVentilatorComponent, GetVerbsEvent<AlternativeVerb>>(OnAlternativeVerbs);
        SubscribeLocalEvent<WolfmedVentilatorComponent, GetVerbsEvent<Verb>>(OnVerbs);
        SubscribeLocalEvent<WolfmedVentilatorComponent, DragDropDraggedEvent>(OnDragDropped);
        SubscribeLocalEvent<WolfmedVentilatorComponent, WolfmedVentilatorAttachDoAfterEvent>(OnAttachDoAfter);
        SubscribeLocalEvent<WolfmedVentilatorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WolfmedVentilatorComponent, EntInsertedIntoContainerMessage>(OnTankInserted);
        SubscribeLocalEvent<WolfmedVentilatorComponent, EntRemovedFromContainerMessage>(OnTankRemoved);
    }

    private void OnMapInit(Entity<WolfmedVentilatorComponent> ent, ref MapInitEvent args)
    {
        UpdateAppearance(ent);
    }

    private void OnShutdown(Entity<WolfmedVentilatorComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Patient is { } patient && !TerminatingOrDeleted(patient))
            RemComp<WolfmedVentilatedComponent>(patient);
    }

    #region Breathing

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedVentilatorComponent>();
        while (query.MoveNext(out var uid, out var ventilator))
        {
            if (ventilator.Patient is not { } patient)
                continue;

            var ent = (uid, ventilator);
            if (!CheckMask(ent, patient) || now < ventilator.NextBreath)
                continue;

            ventilator.NextBreath = now + Period(ventilator);
            Breathe(ent, patient);
        }
    }

    private static TimeSpan Period(WolfmedVentilatorComponent ventilator) =>
        TimeSpan.FromSeconds(MathF.Max(0.1f, ventilator.Interval));

    /// <summary>False when the mask came off: the patient is gone, went into a pod or locker, or is out of reach.</summary>
    private bool CheckMask(Entity<WolfmedVentilatorComponent> ent, EntityUid patient)
    {
        if (TerminatingOrDeleted(patient) || _containers.IsEntityInContainer(patient) ||
            _containers.IsEntityInContainer(ent.Owner))
        {
            Detach(ent, announce: !TerminatingOrDeleted(patient));
            return false;
        }

        if (InReach(ent, patient))
            return true;

        _popup.PopupEntity(Loc.GetString("wolfmed-ventilator-pulled-off", ("ventilator", ent.Owner),
            ("target", Identity.Entity(patient, EntityManager))), patient, PopupType.MediumCaution);
        Detach(ent, announce: false);
        return false;
    }

    /// <summary>
    /// One breath out of the tank and into the room. With gas in it the patient is ventilated until just past the
    /// next one; a dry breath sounds the alarm.
    /// </summary>
    public void Breathe(Entity<WolfmedVentilatorComponent> ent, EntityUid patient)
    {
        var supplying = TakeBreath(ent);
        if (supplying)
        {
            EnsureComp<WolfmedVentilatedComponent>(patient).Until = _timing.CurTime + Period(ent.Comp) * 1.5;
            ent.Comp.DryBreaths = 0;
        }
        else if (ent.Comp.DryBreaths++ % Math.Max(1, ent.Comp.AlarmEvery) == 0)
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-ventilator-alarm", ("ventilator", ent.Owner)), ent.Owner,
                PopupType.MediumCaution);
            _audio.PlayPvs(ent.Comp.AlarmSound, ent.Owner);
        }

        ent.Comp.Supplying = supplying;
    }

    private bool TakeBreath(Entity<WolfmedVentilatorComponent> ent)
    {
        if (GetTank(ent) is not { } tank)
            return false;

        var breath = _gasTank.RemoveAirVolume(tank, Atmospherics.BreathVolume);
        var enough = breath.TotalMoles >= ent.Comp.MinBreathMoles;
        if (_atmosphere.GetContainingMixture(ent.Owner, excite: true) is { } room)
            _atmosphere.Merge(room, breath);

        return enough;
    }

    /// <summary>The fitted tank.</summary>
    public Entity<GasTankComponent>? GetTank(Entity<WolfmedVentilatorComponent> ent)
    {
        return _itemSlots.GetItemOrNull(ent.Owner, WolfmedVentilatorComponent.TankSlotId) is { } tank &&
               TryComp(tank, out GasTankComponent? gasTank)
            ? (tank, gasTank)
            : null;
    }

    #endregion

    #region Attaching

    /// <summary>Starts fitting the mask to <paramref name="patient"/>; false with a popup when it cannot.</summary>
    public bool TryStartAttach(Entity<WolfmedVentilatorComponent> ent, EntityUid patient, EntityUid user)
    {
        if (!CanAttach(ent, patient, user))
            return false;

        _popup.PopupEntity(Loc.GetString("wolfmed-ventilator-attach-begin", ("user", Identity.Entity(user, EntityManager)),
            ("ventilator", ent.Owner), ("target", Identity.Entity(patient, EntityManager))), patient,
            PopupType.SmallCaution);

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.AttachSeconds,
            new WolfmedVentilatorAttachDoAfterEvent(), ent.Owner, target: patient, used: ent.Owner)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private bool CanAttach(Entity<WolfmedVentilatorComponent> ent, EntityUid patient, EntityUid user)
    {
        if (!CanVentilate(patient) || _containers.IsEntityInContainer(patient))
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-ventilator-cannot",
                ("target", Identity.Entity(patient, EntityManager))), user, user);
            return false;
        }

        if (!InReach(ent, patient))
        {
            _popup.PopupEntity(Loc.GetString("wolfmed-ventilator-too-far", ("ventilator", ent.Owner),
                ("target", Identity.Entity(patient, EntityManager))), user, user);
            return false;
        }

        return true;
    }

    /// <summary>Something that breathes: a respirator, and no immunity to needing it.</summary>
    private bool CanVentilate(EntityUid patient) =>
        HasComp<RespiratorComponent>(patient) && !HasComp<BreathingImmunityComponent>(patient);

    private void OnAttachDoAfter(Entity<WolfmedVentilatorComponent> ent, ref WolfmedVentilatorAttachDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } patient || !CanAttach(ent, patient, args.User))
            return;

        args.Handled = true;
        Attach(ent, patient, args.User);
    }

    /// <summary>The mask goes on: any other patient loses it first. Public so a test can skip the do-after.</summary>
    public void Attach(Entity<WolfmedVentilatorComponent> ent, EntityUid patient, EntityUid? user = null)
    {
        if (ent.Comp.Patient is { } previous && previous != patient)
            Detach(ent);

        // One mask to a face: this stand takes over from any other, so no stand's detach cuts off another's patient.
        var others = EntityQueryEnumerator<WolfmedVentilatorComponent>();
        while (others.MoveNext(out var uid, out var other))
        {
            if (uid != ent.Owner && other.Patient == patient)
                Detach((uid, other));
        }

        ent.Comp.Patient = patient;
        ent.Comp.DryBreaths = 0;
        // The first breath lands at once: a patient who needs the stand has no cycle to spare.
        ent.Comp.NextBreath = _timing.CurTime;

        _popup.PopupEntity(Loc.GetString("wolfmed-ventilator-attached", ("ventilator", ent.Owner),
            ("target", Identity.Entity(patient, EntityManager))), patient);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user):user} fitted the mask of {ToPrettyString(ent.Owner):ventilator} to {ToPrettyString(patient):target}");
        UpdateAppearance(ent);
    }

    /// <summary>Takes the mask off.</summary>
    public void Detach(Entity<WolfmedVentilatorComponent> ent, bool announce = true)
    {
        if (ent.Comp.Patient is not { } patient)
            return;

        ent.Comp.Patient = null;
        ent.Comp.Supplying = false;
        if (!TerminatingOrDeleted(patient))
        {
            RemComp<WolfmedVentilatedComponent>(patient);
            if (announce)
            {
                _popup.PopupEntity(Loc.GetString("wolfmed-ventilator-detached", ("ventilator", ent.Owner),
                    ("target", Identity.Entity(patient, EntityManager))), ent.Owner);
            }
        }

        UpdateAppearance(ent);
    }

    private bool InReach(Entity<WolfmedVentilatorComponent> ent, EntityUid patient)
    {
        var from = _xform.GetMapCoordinates(ent.Owner);
        var to = _xform.GetMapCoordinates(patient);
        return from.MapId == to.MapId && (from.Position - to.Position).Length() <= ent.Comp.Range;
    }

    private void OnDragDropped(Entity<WolfmedVentilatorComponent> ent, ref DragDropDraggedEvent args)
    {
        if (args.Handled || !CanVentilate(args.Target))
            return;

        args.Handled = true;
        TryStartAttach(ent, args.Target, args.User);
    }

    #endregion

    #region Verbs, examine and appearance

    private void OnAlternativeVerbs(Entity<WolfmedVentilatorComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Patient == null)
            return;

        // Above the tank's own eject verb, so alt-click takes the mask off before it takes the tank out.
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wolfmed-ventilator-verb-detach"),
            Act = () => Detach(ent),
            Priority = 3,
        });
    }

    /// <summary>The mask onto each patient in reach.</summary>
    private void OnVerbs(Entity<WolfmedVentilatorComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;
        var coords = Transform(ent.Owner).Coordinates;
        foreach (var found in _lookup.GetEntitiesInRange<RespiratorComponent>(coords, ent.Comp.Range))
        {
            var patient = found.Owner;
            if (patient == ent.Comp.Patient || !CanVentilate(patient) || _containers.IsEntityInContainer(patient))
                continue;

            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("wolfmed-ventilator-verb-attach", ("target", Identity.Entity(patient, EntityManager))),
                Act = () => TryStartAttach(ent, patient, user),
                Priority = 2,
            });
        }
    }

    private void OnExamined(Entity<WolfmedVentilatorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(WolfmedVentilatorComponent)))
        {
            args.PushMarkup(GetTank(ent) is { } tank
                ? Loc.GetString("wolfmed-ventilator-examine-tank", ("tank", tank.Owner),
                    ("pressure", MathF.Round(tank.Comp.Air.Pressure)))
                : Loc.GetString("wolfmed-ventilator-examine-no-tank"));

            if (ent.Comp.Patient is not { } patient)
            {
                args.PushMarkup(Loc.GetString("wolfmed-ventilator-examine-not-connected"));
                return;
            }

            args.PushMarkup(Loc.GetString(ent.Comp.Supplying
                    ? "wolfmed-ventilator-examine-breathing"
                    : "wolfmed-ventilator-examine-dry",
                ("target", Identity.Entity(patient, EntityManager))));
        }
    }

    private void OnTankInserted(Entity<WolfmedVentilatorComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == WolfmedVentilatorComponent.TankSlotId)
            UpdateAppearance(ent);
    }

    private void OnTankRemoved(Entity<WolfmedVentilatorComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == WolfmedVentilatorComponent.TankSlotId && !TerminatingOrDeleted(ent))
            UpdateAppearance(ent);
    }

    private void UpdateAppearance(Entity<WolfmedVentilatorComponent> ent)
    {
        if (!TryComp(ent.Owner, out AppearanceComponent? appearance))
            return;

        _appearance.SetData(ent.Owner, WolfmedVentilatorVisuals.Tank, GetTank(ent) != null, appearance);
        _appearance.SetData(ent.Owner, WolfmedVentilatorVisuals.Attached, ent.Comp.Patient != null, appearance);
    }

    #endregion
}
