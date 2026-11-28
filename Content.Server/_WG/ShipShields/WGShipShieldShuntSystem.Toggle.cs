using Content.Server.Shuttles.Components;
using Content.Shared._WG.ShipShields;
using Content.Shared.Shuttles.Components;

namespace Content.Server._WG.ShipShields;

public sealed partial class WGShipShieldShuntSystem
{
    private void InitializeToggle()
    {
        SubscribeNetworkEvent<WGShipShieldToggleEvent>(OnToggle);
        SubscribeNetworkEvent<WGShipVentKeyEvent>(OnVentKey);
    }

    private void OnToggle(WGShipShieldToggleEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user)
            TryToggleFromHelm(user);
    }

    private void OnVentKey(WGShipVentKeyEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is { } user)
            TryVentFromHelm(user);
    }

    /// <summary>
    /// Raises or lowers the shields of the ship a pilot's helm flies, under the same checks as the helm's shield switch.
    /// </summary>
    public bool TryToggleFromHelm(EntityUid pilot)
    {
        if (TryGetPilotGrid(pilot, true) is not { } grid)
            return false;
        _shields.SetWolfgateShieldEnabled(grid, !GetState(grid).Enabled);
        return true;
    }

    /// <summary>Vents the ship a pilot's helm flies, under the same checks as the helm's vent button.</summary>
    public bool TryVentFromHelm(EntityUid pilot)
    {
        return TryGetPilotGrid(pilot, false) is { } grid && _shields.StartWolfgateVent(grid);
    }

    private EntityUid? TryGetPilotGrid(EntityUid pilot, bool needsGenerator)
    {
        return TryComp<PilotComponent>(pilot, out var piloting) && piloting.Console is { } console &&
            TryComp<ShuttleConsoleComponent>(console, out var helm)
            ? TryGetHelmGrid((console, helm), pilot, needsGenerator)
            : null;
    }
}
