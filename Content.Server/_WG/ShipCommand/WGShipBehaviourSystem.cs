using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Chat;
using Content.Shared.Database;
using Robust.Shared.Utility;

namespace Content.Server._WG.ShipCommand;

/// <summary>
/// A commanded ship's standing orders for its roster, set from the command console: where the marines stand, when the
/// gunners open fire, what the pilot does under attack, and the callsign on the air. Whoever is staffed later gets the
/// same orders.
/// </summary>
public sealed class WGShipBehaviourSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGRadioOperatorSystem _radio = default!;

    public const int MaxCallsign = 32;

    /// <summary>A callsign as it goes on the air: one short line with no markup.</summary>
    public static string CleanCallsign(string text)
    {
        return SharedChatSystem.SanitizeAnnouncement(FormattedMessage.RemoveMarkupPermissive(text), MaxCallsign)
            .Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    /// <summary>Changes whichever orders are given, admin-logged, and passes them to the crew aboard.</summary>
    public void Set(Entity<WGShipRosterComponent> grid, WGShipMarineOrders? marines, WGGunnerPolicy? gunners, WGShipPilotStance? pilot,
        string? callsign, EntityUid? actor)
    {
        var comp = grid.Comp;
        comp.MarineOrders = marines ?? comp.MarineOrders;
        comp.GunnerPolicy = gunners ?? comp.GunnerPolicy;
        comp.PilotStance = pilot ?? comp.PilotStance;
        if (callsign != null)
            comp.Callsign = CleanCallsign(callsign);

        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(actor):actor} set standing orders on {ToPrettyString(grid.Owner):grid}: marines {comp.MarineOrders}, gunners {comp.GunnerPolicy}, pilot {comp.PilotStance}, callsign '{comp.Callsign}'");
        ApplyAll(grid);
    }

    /// <summary>Passes the roster's orders to the ship's gunnery and everyone staffed on it.</summary>
    public void ApplyAll(Entity<WGShipRosterComponent> grid)
    {
        EnsureComp<WGGunnerPolicyComponent>(grid).Policy = grid.Comp.GunnerPolicy;
        foreach (var slot in grid.Comp.Slots.Values)
        {
            if (slot.Occupant is { } mob && !TerminatingOrDeleted(mob))
                ApplyTo(grid, mob);
        }
    }

    /// <summary>Passes the roster's orders to one crewman.</summary>
    public void ApplyTo(Entity<WGShipRosterComponent> grid, EntityUid mob)
    {
        if (TryComp<WGPilotDutyComponent>(mob, out var pilot))
        {
            var stance = grid.Comp.PilotStance;
            pilot.ReactToAttacks = stance is WGShipPilotStance.Evade or WGShipPilotStance.Run;
            pilot.FleeAttackers = stance == WGShipPilotStance.Run;
            pilot.FaceThreats = stance == WGShipPilotStance.FaceThreats;
        }

        if (TryComp<WGRadioOperatorComponent>(mob, out var radio))
            _radio.SetCallsign((mob, radio), grid.Comp.Callsign);

        if (TryComp<WGCrewComponent>(mob, out var crew) && crew.Role == WGCrewRoles.Marine)
            ApplyMarine(grid, mob, crew);
    }

    private void ApplyMarine(Entity<WGShipRosterComponent> grid, EntityUid mob, WGCrewComponent crew)
    {
        if (grid.Comp.MarineOrders == WGShipMarineOrders.Patrol)
        {
            RemComp<WGCrewPatrolOrdersComponent>(mob);
            return;
        }

        var orders = EnsureComp<WGCrewPatrolOrdersComponent>(mob);
        orders.Route.Clear();
        if (grid.Comp.MarineOrders == WGShipMarineOrders.Posts)
        {
            if (crew.Post is { } post)
                _crew.SetPost((mob, crew), post);
            return;
        }

        // Every marine walks the same round, each starting from his own post so they stay spread out.
        var slots = grid.Comp.Slots.Values
            .Where(s => s.Kind == WGShipRosterKind.Crew && s.Role == WGCrewRoles.Marine)
            .OrderBy(s => s.Id)
            .ToList();
        orders.Route.AddRange(slots.Select(s => s.Post));
        orders.Next = slots.FindIndex(s => s.Occupant == mob) + 1;
    }
}
