using Content.Server._WG.Administration.Systems;
using Content.Server._WG.Encounters.Components;
using Content.Server.Popups;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Encounters.Systems;

/// <summary>Raised on a grid once it is appraised, so its worth can be changed.</summary>
[ByRefEvent]
public record struct WGGridAppraisedEvent(EntityUid Grid, double Price);

/// <summary>
/// Ship claims: the papers a hulk's chief leaves when he dies, which register the ship to whoever uses them aboard
/// it. A claimed ship is the player's like a bought one, but sells for a fraction of its worth.
/// </summary>
public sealed partial class WGSalvageClaimSystem : EntitySystem
{
    [Dependency] private AdminVesselSpawnSystem _vessels = default!;
    [Dependency] private WGEncounterSystem _encounters = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSalvageClaimDropComponent, MobStateChangedEvent>(OnCarrierState);
        SubscribeLocalEvent<WGSalvageClaimComponent, UseInHandEvent>(OnUse);
        SubscribeLocalEvent<WGSalvagedShipComponent, WGGridAppraisedEvent>(OnAppraised);
    }

    private void OnCarrierState(Entity<WGSalvageClaimDropComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead)
            return;

        var claim = Spawn(ent.Comp.Claim, Transform(ent).Coordinates);
        var papers = EnsureComp<WGSalvageClaimComponent>(claim);
        papers.Ship = ent.Comp.Ship;
        papers.Vessel = ent.Comp.Vessel;
        papers.Resale = ent.Comp.Resale;
        RemComp<WGSalvageClaimDropComponent>(ent);
    }

    private void OnUse(Entity<WGSalvageClaimComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        if (TryClaim(ent, args.User, out var reason))
            return;

        _popup.PopupEntity(Loc.GetString(reason), args.User, args.User);
    }

    /// <summary>Registers the claim's ship to a player standing aboard it. False with the reason's loc key if it can't be.</summary>
    public bool TryClaim(Entity<WGSalvageClaimComponent> claim, EntityUid user, out string reason)
    {
        reason = "wg-salvage-claim-gone";
        var ship = claim.Comp.Ship;
        if (TerminatingOrDeleted(ship))
            return false;

        reason = "wg-salvage-claim-taken";
        if (HasComp<ShuttleDeedComponent>(ship))
            return false;

        reason = "wg-salvage-claim-not-aboard";
        if (Transform(user).GridUid != ship)
            return false;

        reason = "wg-salvage-claim-no-card";
        if (!TryComp<ActorComponent>(user, out var actor) || !_vessels.TryGetDeedCard(actor.PlayerSession, out var card, out _)
            || !_prototypes.TryIndex<VesselPrototype>(claim.Comp.Vessel, out var vessel))
            return false;

        reason = "wg-salvage-claim-failed";
        if (!_vessels.TryAssignOwner(ship, vessel, card, actor.PlayerSession))
            return false;

        EnsureComp<WGSalvagedShipComponent>(ship).Resale = claim.Comp.Resale;
        _encounters.Release(ship);
        _popup.PopupEntity(Loc.GetString("wg-salvage-claim-done", ("ship", Name(ship))), user, user);
        QueueDel(claim);
        return true;
    }

    private void OnAppraised(Entity<WGSalvagedShipComponent> ent, ref WGGridAppraisedEvent args)
    {
        args.Price *= ent.Comp.Resale;
    }
}
