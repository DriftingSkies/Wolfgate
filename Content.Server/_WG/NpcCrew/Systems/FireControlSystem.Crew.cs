using Content.Server._WG.NpcCrew.Systems;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem
{
    [Dependency] private WGCrewFriendlyFireSystem _crewFriendlyFire = default!;

    /// <summary>Records who commands an automatic burst without changing its muzzle position.</summary>
    private void TrackCrewWeaponFire(EntityUid weapon, EntityUid user) => _crewFriendlyFire.TrackWeapon(weapon, user);
}
