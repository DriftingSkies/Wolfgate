using Content.Client._WG.Cockpit;

namespace Content.Client._WG.ShipShields;

public sealed partial class WGShipShieldShuntScreen
{
    /// <summary>Reports shield deployment from the current server snapshot.</summary>
    public WGCockpitStatusReading WgCockpitShieldStatus() => WGCockpitStatusReading.Shield(_state);

    /// <summary>Seconds of gun lock left after a shield collapse, whether heat has the guns holding fire, and the seconds a vent still needs, from the current server snapshot.</summary>
    public (int Seconds, bool Held, int Vent) WgCockpitGunWarning() =>
        (_state?.GunLockSeconds ?? 0, _state is { GunsHeld: true }, _state?.VentSeconds ?? 0);
}
