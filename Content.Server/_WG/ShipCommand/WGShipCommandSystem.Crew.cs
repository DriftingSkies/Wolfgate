using Content.Server._WG.NpcCrew;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.Components;
using Content.Shared._WG.NpcCrew;
using Content.Shared._WG.ShipCommand;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._WG.ShipCommand;

public sealed partial class WGShipCommandSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WGCrewSystem _crew = default!;
    [Dependency] private WGCrewAlertSystem _crewAlerts = default!;

    private void InitializeCrew()
    {
        SubscribeLocalEvent<WGShipCommandComponent, WGCrewAuthorizeEvent>(OnCrewAuthorize);
        SubscribeLocalEvent<WGShipCommandComponent, WGCrewMayTreatEvent>(OnCrewMayTreat);
        SubscribeLocalEvent<WGShipCommandComponent, WGCrewForgiveEvent>(OnCrewForgive);
    }

    /// <summary>The crew take a blow from the Boss without a fight.</summary>
    private void OnCrewForgive(Entity<WGShipCommandComponent> grid, ref WGCrewForgiveEvent args)
    {
        if (!args.Forgiven && IsBoss(args.Attacker, grid))
            args.Forgiven = true;
    }

    /// <summary>
    /// Whether someone is the Boss to the ship's crew: the owner, an officer (an admin on the officers list too), or an
    /// admin while the crew let admins be (<see cref="WGShipCommandComponent.AdminsNeverTargeted"/>) or count them as
    /// officers (<see cref="WGShipCommandComponent.AdminsInZones"/>).
    /// </summary>
    public bool IsBoss(EntityUid person, Entity<WGShipCommandComponent> grid)
    {
        var authority = GetAuthority(person, grid, out var listed);
        return IsBoss(authority, grid.Comp) || IsBoss(listed, grid.Comp);
    }

    /// <summary>Whether a standing makes someone the Boss to a ship's crew; see <see cref="IsBoss(EntityUid, Entity{WGShipCommandComponent})"/>.</summary>
    public static bool IsBoss(WGShipAuthority authority, WGShipCommandComponent command)
    {
        return authority switch
        {
            WGShipAuthority.Owner or WGShipAuthority.Officer => true,
            WGShipAuthority.Admin => command.AdminsNeverTargeted || command.AdminsInZones,
            _ => false,
        };
    }

    /// <summary>The ship's medics refuse the barred and anyone who attacked the crew.</summary>
    private void OnCrewMayTreat(Entity<WGShipCommandComponent> grid, ref WGCrewMayTreatEvent args)
    {
        if (args.PatientUser is { } user && Listed(grid.Comp.Barred, null, user)
            || Exists(args.Patient) && (Attacked(args.Medic, args.Patient) || GetAuthority(args.Patient, grid) == WGShipAuthority.Barred))
            args.Refused = true;
    }

    /// <summary>The ship's crew judge visitors by the visitor's standing and the zone they stand in.</summary>
    private void OnCrewAuthorize(Entity<WGShipCommandComponent> grid, ref WGCrewAuthorizeEvent args)
    {
        if (HasComp<MapGridComponent>(args.Other))
        {
            if (grid.Comp.DocksWelcome)
                args.Authorized = true;
            return;
        }

        args.Authorized = Welcome(grid, args.Crew, args.Other, out var response);
        if (args.Authorized == false)
            args.Response = response;
    }

    /// <summary>
    /// Whether the ship's crew let someone be where they stand. Anyone who attacked the crew is turned away, as are the
    /// barred; admins are let be unless the owner turned that off, then judged as officers while admins may enter
    /// zones, else by the lists like anyone else; Public zones and the open deck admit everyone else; Staff zones need
    /// the staff list or better, Locked and Sealed zones an officer or better. For someone turned away,
    /// <paramref name="response"/> is how the crew answer: the ship's setting for that case.
    /// </summary>
    public bool Welcome(Entity<WGShipCommandComponent> grid, EntityUid crew, EntityUid visitor, out WGCrewSecurityResponse response)
    {
        var command = grid.Comp;
        response = WGCrewSecurityResponse.Ignore;
        if (Attacked(crew, visitor))
        {
            response = WGCrewSecurityResponse.Hostile;
            return false;
        }

        var authority = GetAuthority(visitor, grid, out var listed);
        switch (authority)
        {
            case WGShipAuthority.Owner:
                return true;
            case WGShipAuthority.Admin when command.AdminsNeverTargeted:
                return true;
            case WGShipAuthority.Admin:
                // Without the free pass an admin is judged like an officer where admins may enter zones, else by the lists.
                authority = command.AdminsInZones ? WGShipAuthority.Officer : listed;
                break;
        }

        if (authority == WGShipAuthority.Barred)
        {
            response = command.BarredResponse;
            return false;
        }

        var mode = WGShipZoneMode.Public;
        if (Transform(visitor).GridUid == grid.Owner
            && TryComp<WGShipZonesComponent>(grid, out var zones)
            && TryGetZoneAt(zones, _transform.GetGridTilePositionOrDefault(visitor), out var zone))
            mode = zone.Mode;

        var needed = mode switch
        {
            WGShipZoneMode.Public => WGShipAuthority.Guest,
            WGShipZoneMode.Staff => WGShipAuthority.Staff,
            _ => WGShipAuthority.Officer,
        };
        if (authority >= needed)
            return true;

        response = mode switch
        {
            WGShipZoneMode.Staff => command.StaffZoneResponse,
            WGShipZoneMode.Locked => command.LockedZoneResponse,
            _ => command.SealedZoneResponse,
        };
        return false;
    }

    /// <summary>Whether the visitor attacked this crewman lately, or is one of its crew's shared alert targets.</summary>
    private bool Attacked(EntityUid crew, EntityUid visitor)
    {
        if (TryComp<NPCRetaliationComponent>(crew, out var retaliation))
        {
            var memories = retaliation.AttackMemories;
            if (memories.TryGetValue(visitor, out var until) && _timing.CurTime < until)
                return true;
        }

        return TryComp<WGCrewComponent>(crew, out var member)
               && _crew.HomeGrid(crew, member) is { } home
               && _crewAlerts.IsSharedHostile(home, member.Group, visitor);
    }
}
