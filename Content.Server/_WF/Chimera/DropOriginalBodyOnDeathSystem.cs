using Content.Server.Body.Components;
using Content.Server.Chat.Managers;
using Content.Server.Polymorph.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Polymorph;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Chimera;

/// <summary>
/// Drops the original body out of a dead polymorph with <see cref="DropOriginalBodyOnDeathComponent"/>.
/// </summary>
public sealed partial class DropOriginalBodyOnDeathSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DropOriginalBodyOnDeathComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<DropOriginalBodyOnDeathComponent, BeforeGibbedEvent>(OnBeforeGibbed);
        SubscribeLocalEvent<DropOriginalBodyOnDeathComponent, PolymorphedEvent>(OnPolymorphed);
        SubscribeLocalEvent<ChimeraTurnCooldownComponent, ExaminedEvent>(OnCooldownExamined);
    }

    /// <summary>Whether Letoferol may turn this body; false while its turn cooldown runs.</summary>
    public bool CanTurn(EntityUid uid)
    {
        return !TryComp<ChimeraTurnCooldownComponent>(uid, out var cooldown) || _timing.CurTime >= cooldown.ReadyAt;
    }

    private void OnCooldownExamined(Entity<ChimeraTurnCooldownComponent> ent, ref ExaminedEvent args)
    {
        var left = ent.Comp.ReadyAt - _timing.CurTime;
        if (left <= TimeSpan.Zero)
            return;

        args.PushMarkup(Loc.GetString("wf-chimera-turn-cooldown-examine", ("time", FormatTime(left))));
    }

    private string FormatTime(TimeSpan time)
    {
        return Loc.GetString("wf-chimera-turn-cooldown-time",
            ("minutes", (int) time.TotalMinutes),
            ("seconds", time.Seconds));
    }

    private void OnMobStateChanged(Entity<DropOriginalBodyOnDeathComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            DropOriginal(ent);
    }

    /// <summary>Gibbing a living mob skips its death, so the body drops before the gib instead.</summary>
    private void OnBeforeGibbed(Entity<DropOriginalBodyOnDeathComponent> ent, ref BeforeGibbedEvent args)
    {
        DropOriginal(ent);
    }

    /// <summary>
    /// A cure reverts the mob and deletes it. Its polymorph keeps the victim's gear on the body instead of dropping
    /// anything, so what the mob carries is dropped here rather than deleted with it.
    /// </summary>
    private void OnPolymorphed(Entity<DropOriginalBodyOnDeathComponent> ent, ref PolymorphedEvent args)
    {
        // DropOriginal removes the polymorph before raising this, and its corpse keeps what it carries.
        if (!args.IsRevert || !HasComp<PolymorphedEntityComponent>(ent))
            return;

        if (_inventory.TryGetContainerSlotEnumerator(ent.Owner, out var slots))
        {
            while (slots.MoveNext(out var slot))
            {
                _inventory.TryUnequip(ent, slot.ID, true, true);
            }
        }

        foreach (var held in _hands.EnumerateHeld(ent))
        {
            _hands.TryDrop(ent, held, checkActionBlocker: false);
        }

        if (TryComp<StorageComponent>(ent, out var storage))
            _container.EmptyContainer(storage.Container, true);
    }

    /// <summary>
    /// Puts the body the mob was polymorphed from next to it, purged and holding the mob's mind. The corpse stops being
    /// a polymorph, so nothing reverts it later, and drops the person's name.
    /// </summary>
    public void DropOriginal(Entity<DropOriginalBodyOnDeathComponent> ent)
    {
        if (!TryComp<PolymorphedEntityComponent>(ent, out var polymorphed))
            return;

        var original = polymorphed.Parent;
        var config = polymorphed.Configuration;

        // Removed first: a revert-on-death polymorph would otherwise swap the corpse for the body next update.
        RemComp(ent, polymorphed);

        if (TerminatingOrDeleted(original))
            return;

        Purge(original, ent.Comp.PurgedReagents);
        _transform.DropNextTo(original, ent.Owner);

        if (ent.Comp.TurnCooldown > TimeSpan.Zero)
            EnsureComp<ChimeraTurnCooldownComponent>(original).ReadyAt = _timing.CurTime + ent.Comp.TurnCooldown;

        if (_mind.TryGetMind(ent, out var mindId, out var mind))
        {
            _mind.TransferTo(mindId, original, mind: mind);

            if (ent.Comp.TurnCooldown > TimeSpan.Zero && _player.TryGetSessionById(mind.UserId, out var session))
            {
                _chat.DispatchServerMessage(session,
                    Loc.GetString("wf-chimera-turn-cooldown-message", ("time", FormatTime(ent.Comp.TurnCooldown))));
            }
        }

        // The person's name leaves the corpse with them.
        if (config.TransferName && Prototype(ent) is { } proto)
            _metaData.SetEntityName(ent, proto.Name);

        var ev = new PolymorphedEvent(ent, original, true);
        RaiseLocalEvent(ent, ref ev);

        _audio.PlayPvs(config.ExitPolymorphSound, original);
        _popup.PopupEntity(Loc.GetString(ent.Comp.Popup, ("body", Identity.Entity(original, EntityManager))),
            original,
            PopupType.Medium);
    }

    /// <summary>Removes the reagents from the body's own solutions and those of its organs, such as the stomach.</summary>
    private void Purge(EntityUid body, List<ProtoId<ReagentPrototype>> reagents)
    {
        if (reagents.Count == 0)
            return;

        PurgeSolutions(body, reagents);
        foreach (var organ in _body.GetBodyOrgans(body))
        {
            PurgeSolutions(organ.Id, reagents);
        }
    }

    private void PurgeSolutions(EntityUid uid, List<ProtoId<ReagentPrototype>> reagents)
    {
        foreach (var (_, soln) in _solution.EnumerateSolutions(uid))
        {
            var solution = soln.Comp.Solution;
            var removed = false;
            foreach (var reagent in reagents)
            {
                var quantity = solution.GetTotalPrototypeQuantity(reagent);
                if (quantity <= FixedPoint2.Zero)
                    continue;

                // Blood-borne copies carry DNA data, so match on the prototype alone.
                solution.RemoveReagent(reagent, quantity, ignoreReagentData: true);
                removed = true;
            }

            if (removed)
                _solution.UpdateChemicals(soln);
        }
    }
}
