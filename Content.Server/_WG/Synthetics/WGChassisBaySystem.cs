using System.Diagnostics.CodeAnalysis;
using Content.Server._EinsteinEngines.Silicon.Charge;
using Content.Server._HL.Silicons.Synths.Battery;
using Content.Server._WG.Wolfmed.Life;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._HL.Silicons.Synths.Battery;
using Content.Shared._WG.Synthetics;
using Content.Shared._WG.Wolfmed.Body;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Synthetics;

/// <summary>
/// The chassis bay: a synthetic body's torso plating, opened by its owner from the panel or by anyone with the verb.
/// While it is open the components inside are pulled and seated by hand, the chassis is slower, and every hit on the
/// torso reaches them. The power cell comes out and goes in the same way whether it sits in a synth's battery organ
/// slot or in an IPC chassis's locked cell slot, whose lock the open bay gets round.
/// </summary>
public sealed partial class WGChassisBaySystem : SharedWGSyntheticsSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SiliconChargeSystem _siliconCharge = default!;
    [Dependency] private SynthBatteryPowerSystem _synthPower = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private WGSyntheticLimbSystem _limbs = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SynthBatterySystem _synthBattery = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan BayDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ComponentDelay = TimeSpan.FromSeconds(2);

    /// <summary>Working on your own chassis takes this much longer.</summary>
    private const float SelfMultiplier = 2f;

    private static readonly SoundSpecifier BaySound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");
    private static readonly SoundSpecifier ComponentSound = new SoundPathSpecifier("/Audio/Items/crowbar.ogg");

    /// <summary>Reach lines an open bay zeroes when the torso had none of its own.</summary>
    private static readonly ProtoId<DamageTypePrototype>[] ExposedTypes = ["Piercing", "Slash", "Blunt", "Heat", "Shock"];

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGChassisComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<WGChassisComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WGChassisComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<WGChassisComponent, WGChassisBayDoAfterEvent>(OnBayDoAfter);
        SubscribeLocalEvent<WGChassisComponent, WGChassisPullOrganDoAfterEvent>(OnPullDoAfter);
        SubscribeLocalEvent<WGChassisComponent, WGChassisInstallOrganDoAfterEvent>(OnInstallDoAfter);

        // The panel's other messages on this key are WGChassisPanelSystem's.
        Subs.BuiEvents<WGChassisComponent>(WGChassisPanelUiKey.Key, subs =>
        {
            subs.Event<WGChassisToggleBayMessage>(OnToggleBayMessage);
            subs.Event<WGChassisDisconnectOrganMessage>(OnDisconnectMessage);
        });
    }

    #region Bay

    /// <summary>Starts opening or closing the bay. False when nothing started.</summary>
    public bool TryStartToggle(EntityUid body, EntityUid user, bool open)
    {
        if (!TryComp(body, out WGChassisComponent? chassis) || chassis.BayOpen == open)
            return false;

        if (!IsSynthetic(body))
        {
            _popup.PopupEntity(Loc.GetString("wg-synthetics-not-synthetic"), body, user);
            return false;
        }

        if (GetTorso(body) == null)
        {
            _popup.PopupEntity(Loc.GetString("wg-chassis-bay-no-torso"), body, user);
            return false;
        }

        var self = user == body;
        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            self ? BayDelay * SelfMultiplier : BayDelay,
            new WGChassisBayDoAfterEvent(open),
            body,
            body)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (!started)
            return false;

        var target = Identity.Entity(body, EntityManager);
        if (self)
        {
            _popup.PopupEntity(Loc.GetString(open ? "wg-chassis-bay-opening-self" : "wg-chassis-bay-closing-self"), body, user);
        }
        else
        {
            _popup.PopupEntity(Loc.GetString(open ? "wg-chassis-bay-opening" : "wg-chassis-bay-closing", ("target", target)), body, user);
            _popup.PopupEntity(Loc.GetString(open ? "wg-chassis-bay-opening-other" : "wg-chassis-bay-closing-other",
                ("user", Identity.Entity(user, EntityManager))), body, body, PopupType.MediumCaution);
        }

        return true;
    }

    private void OnBayDoAfter(Entity<WGChassisComponent> ent, ref WGChassisBayDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;
        if (!IsSynthetic(ent) || GetTorso(ent) == null)
            return;

        SetBay(ent, args.Open);
    }

    /// <summary>Opens or closes the bay at once, with no do-after.</summary>
    public void SetBay(Entity<WGChassisComponent> ent, bool open)
    {
        if (ent.Comp.BayOpen == open)
            return;

        ent.Comp.BayOpen = open;
        Dirty(ent);

        if (GetTorso(ent) is { } torso)
            SetExposed(torso, open);

        _movement.RefreshMovementSpeedModifiers(ent);
        _audio.PlayPvs(BaySound, ent);
        _popup.PopupEntity(Loc.GetString(open ? "wg-chassis-bay-opened" : "wg-chassis-bay-closed"), ent);
    }

    /// <summary>
    /// While the bay is open every hit on the torso reaches the components: a zero reach line passes any hit
    /// (WolfmedOrganThresholdSystem.GetExcess). The torso's own lines are kept and given back on close.
    /// </summary>
    private void SetExposed(EntityUid torso, bool open)
    {
        if (!TryComp(torso, out WolfmedBodyPartComponent? data))
            return;

        if (!open)
        {
            if (!TryComp(torso, out WGChassisBayExposedComponent? exposed))
                return;

            data.OrganReach = exposed.OriginalReach;
            RemComp<WGChassisBayExposedComponent>(torso);
            return;
        }

        if (HasComp<WGChassisBayExposedComponent>(torso))
            return;

        AddComp(torso, new WGChassisBayExposedComponent { OriginalReach = data.OrganReach });
        var zeroed = new Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2>();
        IEnumerable<ProtoId<DamageTypePrototype>> types = data.OrganReach.Count > 0 ? data.OrganReach.Keys : ExposedTypes;
        foreach (var type in types)
            zeroed[type] = FixedPoint2.Zero;

        data.OrganReach = zeroed;
    }

    private void OnToggleBayMessage(Entity<WGChassisComponent> ent, ref WGChassisToggleBayMessage args) =>
        TryStartToggle(ent, args.Actor, !ent.Comp.BayOpen);

    #endregion

    #region Components

    /// <summary>Every component that can be pulled from the body right now: its organs and its power cell.</summary>
    public List<EntityUid> GetComponents(EntityUid body)
    {
        var found = new List<EntityUid>();
        foreach (var (organ, _) in _body.GetBodyOrgans(body))
            found.Add(organ);

        if (TryGetCellContainer(body, out var container))
        {
            foreach (var contained in container.ContainedEntities)
            {
                if (HasComp<PowerCellComponent>(contained))
                    found.Add(contained);
            }
        }

        return found;
    }

    /// <summary>Starts pulling a component out of an open bay. False when nothing started.</summary>
    public bool TryStartPull(EntityUid body, EntityUid item, EntityUid user)
    {
        if (!TryComp(body, out WGChassisComponent? chassis))
            return false;

        if (!IsSynthetic(body))
        {
            _popup.PopupEntity(Loc.GetString("wg-synthetics-not-synthetic"), body, user);
            return false;
        }

        if (!chassis.BayOpen)
        {
            _popup.PopupEntity(Loc.GetString("wg-chassis-bay-closed-refusal"), body, user);
            return false;
        }

        if (!IsInstalled(body, item))
            return false;

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            user == body ? ComponentDelay * SelfMultiplier : ComponentDelay,
            new WGChassisPullOrganDoAfterEvent(GetNetEntity(item)),
            body,
            body)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (!started)
            return false;

        _popup.PopupEntity(Loc.GetString("wg-chassis-bay-pulling", ("item", item)), body, user);
        if (user != body)
        {
            _popup.PopupEntity(Loc.GetString("wg-chassis-bay-pulling-other",
                ("user", Identity.Entity(user, EntityManager)), ("item", item)), body, body, PopupType.MediumCaution);
        }

        return true;
    }

    private void OnPullDoAfter(Entity<WGChassisComponent> ent, ref WGChassisPullOrganDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !TryGetEntity(args.Organ, out var item))
            return;

        args.Handled = TryPull(ent, item.Value, args.Args.User);
    }

    /// <summary>Takes a component out of an open bay into the user's hand, or onto the floor beside the body.</summary>
    public bool TryPull(Entity<WGChassisComponent> body, EntityUid item, EntityUid user)
    {
        if (!body.Comp.BayOpen || !IsSynthetic(body))
            return false;

        // Taking the core out kills the mind and taking the pump out shuts the chassis down; both follow from the
        // organ leaving its slot (WolfmedLifeSystem.OnOrganRemoved), as they do in surgery.
        if (TryComp(item, out OrganComponent? organ))
        {
            if (organ.Body != body.Owner || !_body.RemoveOrgan(item, organ))
                return false;
        }
        else if (!IsCell(body, item, out var container) || !_containers.Remove(item, container))
        {
            return false;
        }
        else
        {
            RefreshPower(body);
        }

        if (!_hands.TryPickupAnyHand(user, item))
            _transform.DropNextTo(item, body.Owner);

        _audio.PlayPvs(ComponentSound, body);
        _popup.PopupEntity(Loc.GetString("wg-chassis-bay-pulled", ("item", item)), body, user);
        if (user != body.Owner)
            _popup.PopupEntity(Loc.GetString("wg-chassis-bay-pulled-other", ("item", item)), body, body, PopupType.MediumCaution);

        return true;
    }

    private void OnDisconnectMessage(Entity<WGChassisComponent> ent, ref WGChassisDisconnectOrganMessage args)
    {
        if (TryGetEntity(args.Organ, out var organ))
            TryStartPull(ent, organ.Value, args.Actor);
    }

    /// <summary>Starts seating an organ or a power cell into its empty slot. False when nothing started.</summary>
    public bool TryStartInstall(EntityUid body, EntityUid item, EntityUid user)
    {
        if (!TryComp(body, out WGChassisComponent? chassis))
            return false;

        if (!IsSynthetic(body))
        {
            _popup.PopupEntity(Loc.GetString("wg-synthetics-not-synthetic"), body, user);
            return false;
        }

        if (!chassis.BayOpen)
        {
            _popup.PopupEntity(Loc.GetString("wg-chassis-bay-closed-refusal"), body, user);
            return false;
        }

        var refusal = FindSlot(body, item, out var part, out var slot);
        if (refusal != null)
        {
            _popup.PopupEntity(Loc.GetString(refusal), body, user);
            return false;
        }

        var started = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager,
            user,
            user == body ? ComponentDelay * SelfMultiplier : ComponentDelay,
            new WGChassisInstallOrganDoAfterEvent(GetNetEntity(item), GetNetEntity(part), slot),
            body,
            body,
            item)
        {
            BreakOnMove = true,
            NeedHand = true,
        });

        if (started)
            _popup.PopupEntity(Loc.GetString("wg-chassis-bay-installing", ("item", item)), body, user);

        return started;
    }

    private void OnInstallDoAfter(Entity<WGChassisComponent> ent, ref WGChassisInstallOrganDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !TryGetEntity(args.Organ, out var item))
            return;

        args.Handled = TryInstall(ent, item.Value, args.Args.User);
    }

    /// <summary>Seats an organ or a power cell into its empty slot in an open bay.</summary>
    public bool TryInstall(Entity<WGChassisComponent> body, EntityUid item, EntityUid user)
    {
        // Re-read the slot: it may have been filled or the bay shut during the do-after.
        if (!body.Comp.BayOpen || !IsSynthetic(body) || FindSlot(body, item, out var part, out var slot) != null)
            return false;

        // InsertOrgan raises OrganAddedToBodyEvent, which is what restarts a pump or brings a core online, as the
        // surgery's insert step does. A cell is a plain container insert, as SynthSurgerySystem's is; for an IPC
        // chassis that goes round the slot's lock on purpose.
        if (TryComp(item, out OrganComponent? organ))
        {
            if (!_body.InsertOrgan(part, item, slot, organ: organ))
                return false;
        }
        else if (!TryGetCellContainer(body, out var container) || !_containers.Insert(item, container))
        {
            return false;
        }
        else
        {
            RefreshPower(body);
        }

        _audio.PlayPvs(ComponentSound, body);
        _popup.PopupEntity(Loc.GetString("wg-chassis-bay-installed", ("item", item)), body, user);
        if (user != body.Owner)
            _popup.PopupEntity(Loc.GetString("wg-chassis-bay-installed-other", ("item", item)), body, body);

        return true;
    }

    /// <summary>
    /// The part and slot an organ or a power cell would go into, or the loc key of the refusal: no such slot on this
    /// body, or every one of them taken.
    /// </summary>
    private string? FindSlot(EntityUid body, EntityUid item, out EntityUid part, out string slot)
    {
        part = default;
        slot = string.Empty;

        if (HasComp<PowerCellComponent>(item) && TryComp(body, out SynthBatteryComponent? synth))
        {
            if (!_synthBattery.TryGetBatteryContainer(body, synth.OrganSlot, out var holder, out var container))
                return "wg-chassis-bay-no-slot";

            if (container.ContainedEntities.Count > 0)
                return "wg-chassis-bay-slot-full";

            part = holder.Value.Owner;
            slot = synth.OrganSlot;
            return null;
        }

        if (HasComp<PowerCellComponent>(item) && TryGetCellSlot(body, out var cellSlot))
        {
            if (cellSlot.HasItem)
                return "wg-chassis-bay-slot-full";

            if (_whitelist.IsWhitelistFail(cellSlot.Whitelist, item) || _whitelist.IsBlacklistPass(cellSlot.Blacklist, item))
                return "wg-chassis-bay-no-slot";

            part = GetTorso(body) ?? body;
            slot = cellSlot.ContainerSlot?.ID ?? string.Empty;
            return null;
        }

        if (!TryComp(item, out OrganComponent? organ) || organ.Body != null)
            return "wg-chassis-bay-no-slot";

        var exists = false;
        foreach (var (partId, partComp) in _body.GetBodyChildren(body))
        {
            if (!partComp.Organs.ContainsKey(organ.SlotId))
                continue;

            exists = true;
            if (_containers.TryGetContainer(partId, SharedBodySystem.GetOrganContainerId(organ.SlotId), out var container) &&
                container.ContainedEntities.Count > 0)
                continue;

            part = partId;
            slot = organ.SlotId;
            return null;
        }

        return exists ? "wg-chassis-bay-slot-full" : "wg-chassis-bay-no-slot";
    }

    /// <summary>Whether the item is installed in this body: one of its organs, or its power cell.</summary>
    private bool IsInstalled(EntityUid body, EntityUid item) =>
        TryComp(item, out OrganComponent? organ) ? organ.Body == body : IsCell(body, item, out _);

    private bool IsCell(EntityUid body, EntityUid item, [NotNullWhen(true)] out BaseContainer? container) =>
        TryGetCellContainer(body, out container) && HasComp<PowerCellComponent>(item) && container.Contains(item);

    /// <summary>Where the cell sits: a synth's battery organ slot, or the container of an IPC chassis's cell slot.</summary>
    private bool TryGetCellContainer(EntityUid body, [NotNullWhen(true)] out BaseContainer? container)
    {
        container = null;
        if (TryComp(body, out SynthBatteryComponent? synth))
            return _synthBattery.TryGetBatteryContainer(body, synth.OrganSlot, out _, out container);

        if (!TryGetCellSlot(body, out var slot) || slot.ContainerSlot is not { } cellContainer)
            return false;

        container = cellContainer;
        return true;
    }

    /// <summary>An IPC chassis's own cell slot (PowerCellSlot), locked while the chassis is shut.</summary>
    private bool TryGetCellSlot(EntityUid body, [NotNullWhen(true)] out ItemSlot? slot)
    {
        slot = null;
        return !HasComp<SynthBatteryComponent>(body) && TryComp(body, out PowerCellSlotComponent? cellSlot) &&
               _itemSlots.TryGetSlot(body, cellSlot.CellSlotId, out slot);
    }

    /// <summary>
    /// Tells the chassis's battery tracking at once that its cell came or went, so low power starts or ends on the
    /// same tick. The IPC charge loop skips a body with no mind, so it can't be left to that.
    /// </summary>
    private void RefreshPower(EntityUid body)
    {
        if (TryComp(body, out SiliconComponent? silicon) && silicon.BatteryPowered)
        {
            short state = 0;
            if (_siliconCharge.TryGetSiliconBattery(body, out var battery) && battery.MaxCharge > 0f)
                state = (short) MathF.Round(battery.CurrentCharge / battery.MaxCharge * 10f);

            _siliconCharge.UpdateChargeState(body, state, silicon);
        }

        if (TryComp(body, out SynthBatteryComponent? synth))
        {
            var charged = _synthBattery.TryGetBattery(body, out var cell, synth) && cell.Value.Comp.CurrentCharge > 0f;
            _synthPower.SetUnpowered((body, synth), !charged);
        }

        _shutdown.Refresh(body);
    }

    #endregion

    #region Interaction

    private void OnInteractUsing(Entity<WGChassisComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        // One owner for this pair, so a limb comes back on through the same handler.
        if (HasComp<BodyPartComponent>(args.Used))
        {
            args.Handled = _limbs.TryStartReattach(ent, args.Used, args.User);
            return;
        }

        // A synth's battery organ slot is always ours. An IPC chassis's cell slot is ours while it is locked or the
        // bay is open; unlocked with the bay shut, ItemSlots takes the cell as it always did.
        var cell = HasComp<PowerCellComponent>(args.Used) &&
                   (HasComp<SynthBatteryComponent>(ent) ||
                    TryGetCellSlot(ent, out var cellSlot) && (cellSlot.Locked || ent.Comp.BayOpen));
        if (!HasComp<OrganComponent>(args.Used) && !cell)
            return;

        args.Handled = true;
        TryStartInstall(ent, args.Used, args.User);
    }

    private void OnGetVerbs(Entity<WGChassisComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null || !IsSynthetic(ent))
            return;

        var user = args.User;
        var open = ent.Comp.BayOpen;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(open ? "wg-chassis-bay-verb-close" : "wg-chassis-bay-verb-open"),
            Act = () => TryStartToggle(ent, user, !open),
            Priority = -1,
        });

        if (!open || !_hands.TryGetEmptyHand(user, out _))
            return;

        foreach (var item in GetComponents(ent))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("wg-chassis-bay-verb-pull", ("item", item)),
                Act = () => TryStartPull(ent, item, user),
                Priority = -2,
            });
        }
    }

    private void OnExamined(Entity<WGChassisComponent> ent, ref ExaminedEvent args)
    {
        if (!IsSynthetic(ent))
            return;

        var target = Identity.Entity(ent, EntityManager);
        if (ent.Comp.BayOpen)
            args.PushMarkup(Loc.GetString("wg-chassis-bay-examine-open", ("target", target)));

        _limbs.AddLooseExamine(ent, target, args);
    }

    #endregion

    /// <summary>The body's torso, if it still has one.</summary>
    private EntityUid? GetTorso(EntityUid body)
    {
        foreach (var (part, _) in _body.GetBodyChildrenOfType(body, BodyPartType.Torso))
            return part;

        return null;
    }
}
