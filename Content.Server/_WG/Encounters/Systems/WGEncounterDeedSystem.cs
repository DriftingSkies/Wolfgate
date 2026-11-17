using Content.Server._WG.Encounters.Components;
using Content.Server._WG.ShipAccess;
using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Server.Administration.Logs;
using Robust.Shared.Player;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>
/// Encounter ship deeds: the highest in command of an encounter ship drops its deed when he dies, and whoever uses it
/// makes the ship permanent, cut loose from its encounter.
/// </summary>
public sealed partial class WGEncounterDeedSystem : EntitySystem
{
    [Dependency] private WGEncounterSystem _encounters = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private WGShipAccessServerSystem _shipAccess = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGEncounterDeedDropComponent, MobStateChangedEvent>(OnCarrierState);
        SubscribeLocalEvent<WGEncounterDeedComponent, UseInHandEvent>(OnUse);
    }

    private void OnCarrierState(Entity<WGEncounterDeedDropComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var ship = ent.Comp.Ship;
        var deedId = ent.Comp.Deed;
        RemComp<WGEncounterDeedDropComponent>(ent);
        if (TerminatingOrDeleted(ship) || !HasComp<WGEncounterGridComponent>(ship))
            return;

        var deed = Spawn(deedId, Transform(ent).Coordinates);
        EnsureComp<WGEncounterDeedComponent>(deed).Ship = ship;
        _meta.SetEntityName(deed, Loc.GetString("wg-encounter-deed-name", ("ship", Name(ship))));
    }

    private void OnUse(Entity<WGEncounterDeedComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var ship = ent.Comp.Ship;
        if (TerminatingOrDeleted(ship))
        {
            _popup.PopupEntity(Loc.GetString("wg-encounter-deed-gone"), args.User, args.User);
            return;
        }

        if (!_encounters.Free(ship))
        {
            _popup.PopupEntity(Loc.GetString("wg-encounter-deed-already", ("ship", Name(ship))), args.User, args.User);
            return;
        }

        // The claimant's account owns it now, so their card opens it rather than the dead crew's.
        if (TryComp<ActorComponent>(args.User, out var actor))
            _shipAccess.SetupRegisteredShip(ship, Name(args.User), new[] { actor.PlayerSession.UserId });

        _adminLog.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(args.User):player} claimed encounter ship {ToPrettyString(ship):grid} with its deed");
        _popup.PopupEntity(Loc.GetString("wg-encounter-deed-done", ("ship", Name(ship))), args.User, args.User);
        QueueDel(ent);
    }
}
