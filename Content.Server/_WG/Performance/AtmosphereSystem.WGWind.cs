using Content.Server.Atmos;
using Content.Shared.Atmos.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    private bool _wgWindSkipWeak;
    private int _wgWindSearches;
    private int _wgWindSkips;

    /// <summary>
    /// Whether a high-pressure tile's wind is too weak to move anything, so its entity search can be skipped.
    /// <see cref="ExperiencePressureDifference"/> pushes only past <see cref="MovedByPressureComponent.ThrowForce"/>,
    /// and the softer mob check needs <see cref="MovedByPressureComponent.MoveResist"/>, which is never below it
    /// (100 by default, no prototype lowers it).
    /// </summary>
    private bool WGWindTooWeak(TileAtmosphere tile)
    {
        if (!_wgWindSkipWeak || WGWindCanMove(tile.PressureDifference))
        {
            _wgWindSearches++;
            return false;
        }

        _wgWindSkips++;
        return true;
    }

    /// <summary>Whether wind of this pressure difference reaches the force <see cref="ExperiencePressureDifference"/> needs.</summary>
    public static bool WGWindCanMove(float pressureDifference)
    {
        return MathF.Sqrt(pressureDifference) * 2.25f >= MovedByPressureComponent.ThrowForce;
    }
}
