using Content.Server._WG.NpcCrew.Components;
using Content.Shared._WG.ShipShields;

namespace Content.Server._WG.NpcCrew.Systems;

/// <summary>Extends autonomous crew friendly-fire protection to shield interception.</summary>
public sealed partial class WGCrewShieldProtectionSystem : EntitySystem
{
    [Dependency] private WGCrewFriendlyFireSystem _friendlyFire = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShipShieldInterceptAttemptEvent>(OnIntercept);
    }

    private void OnIntercept(ref WGShipShieldInterceptAttemptEvent args)
    {
        if (args.Cancelled)
            return;
        // Launched rounds retain their controller even after the cannon changes hands.
        var source = HasComp<WGCrewShipFireComponent>(args.Shot) ? args.Shot
            : args.Weapon is { } weapon && HasComp<WGCrewShipFireComponent>(weapon) ? weapon
            : args.Shooter ?? args.Weapon;
        if (source is { } attacker && _friendlyFire.Protected(attacker, args.Grid))
            args.Cancelled = true;
    }
}
