using System.Numerics;
using Content.Server._WG.Encounters;
using Content.Server._WG.Encounters.Components;
using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.ReaverCampaign.Components;
using Robust.Shared.Player;

namespace Content.Server._WG.ReaverCampaign.Systems;

public sealed partial class WGReaverCampaignSystem
{
    /// <summary>How far from a boarded ship the ship its boarders came from is looked for.</summary>
    public const float BoardersShipRange = 3000f;

    /// <summary>Whether a grid is an encounter's ship still in its crew's hands; one boarders seized is a player ship to the campaign.</summary>
    private bool NpcHull(EntityUid grid)
    {
        return HasComp<WGEncounterGridComponent>(grid) && !HasComp<WGCrewSeizedHullComponent>(grid);
    }

    /// <summary>
    /// A campaign crew that finds players aboard calls for help: a hub, or a reinforcement of one, calls the patrols
    /// nearby back as it does when hit, and a patrol is sent after the nearest crewed ship, taken for the boarders' own.
    /// </summary>
    private void OnCrewAlert(ref WGCrewAlertEvent args)
    {
        if (!Running || Held || !IsCampaignShip(args.Grid) || !PlayerAboard(args.Grid, args.Hostiles))
            return;

        var owner = Comp<WGEncounterGridComponent>(args.Grid).Encounter;
        if (TryComp<WGReaverReinforcementComponent>(owner, out var reinforcement))
            owner = reinforcement.Stronghold;

        if (TryComp<WGReaverStrongholdComponent>(owner, out var stronghold) && TryComp<WGEncounterComponent>(owner, out var encounter)
            && encounter.Resolution == null && RecallPatrols((owner, stronghold, encounter)) > 0)
            stronghold.Recalled = true;

        if (BoardersShip(args.Grid) is { } ship && TryIntercept(ship))
            Log.Info($"Reaver ship {ToPrettyString(args.Grid)} was boarded; a patrol goes after {ToPrettyString(ship)}.");
    }

    /// <summary>A hull taken from the campaign is hunted at once, and for as long as boarders hold it.</summary>
    private void OnShipSeized(ref WGEncounterShipSeizedEvent args)
    {
        // Taken while the campaign is held, it is remembered and hunted once the campaign runs again.
        if (!IsCampaignEncounter(args.Encounter) || State(true) is not { } state)
            return;

        state.Stolen[args.Grid] = _timing.CurTime + StolenRetry;
        if (Running && !Held)
            TryRecover(args.Grid);
    }

    /// <summary>Whether any of a crew's new hostiles is a player standing on its ship.</summary>
    private bool PlayerAboard(EntityUid grid, EntityUid[] hostiles)
    {
        foreach (var hostile in hostiles)
        {
            if (HasComp<ActorComponent>(hostile) && Transform(hostile).GridUid == grid)
                return true;
        }

        return false;
    }

    /// <summary>The nearest crewed ship the campaign deals with inside <see cref="BoardersShipRange"/> of a boarded ship, if any.</summary>
    private EntityUid? BoardersShip(EntityUid boarded)
    {
        var at = _transform.GetWorldPosition(boarded);
        EntityUid? nearest = null;
        var best = BoardersShipRange * BoardersShipRange;
        foreach (var ship in Crewed())
        {
            var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(ship), at);
            if (distance > best)
                continue;

            best = distance;
            nearest = ship;
        }

        return nearest;
    }
}
