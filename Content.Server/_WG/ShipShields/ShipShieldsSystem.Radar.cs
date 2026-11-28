using Content.Server._WG.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WG.ShipShields;
using Robust.Shared.Physics.Components;

namespace Content.Server._Crescent.ShipShields;

// What radars draw of a ship goes on its grid, which reaches every client: the overload mark and the raised field's outline.
public sealed partial class ShipShieldsSystem
{
    private const float RadarMarkInterval = 0.25f;

    /// <summary>Steps the radar copy of field health moves in, so ordinary hits don't resend it to every client.</summary>
    private const float RadarHealthStep = 0.05f;

    private float _wgRadarMarkAccumulator;
    private readonly HashSet<EntityUid> _wgRadarShielded = new();

    /// <summary>Whether a ship is overloaded: its cooling generator is waiting out an overload or a collapse has locked its guns.</summary>
    public bool IsWolfgateShipOverloaded(EntityUid grid)
    {
        if (GetWolfgateGunLockSeconds(grid) > 0)
            return true;
        return TryComp<WGShipHeatComponent>(grid, out var hull) && hull.Cooler is { } cooler && !Deleted(cooler) &&
            Transform(cooler).GridUid == grid && IsWolfgateOverloaded(cooler);
    }

    private void UpdateWolfgateRadarMarks(float frameTime)
    {
        _wgRadarMarkAccumulator += frameTime;
        if (_wgRadarMarkAccumulator < RadarMarkInterval)
            return;
        _wgRadarMarkAccumulator = 0f;
        UpdateWolfgateOverloadMarks();
        UpdateWolfgateShieldRadar();
    }

    private void UpdateWolfgateOverloadMarks()
    {
        var query = EntityQueryEnumerator<WGShipHeatComponent>();
        while (query.MoveNext(out var grid, out _))
        {
            var marked = HasComp<WGShipOverloadedComponent>(grid);
            var overloaded = IsWolfgateShipOverloaded(grid);
            if (overloaded && !marked)
                EnsureComp<WGShipOverloadedComponent>(grid);
            else if (!overloaded && marked)
                RemComp<WGShipOverloadedComponent>(grid);
        }
    }

    /// <summary>Copies each raised field's outline, coarse health and colour onto its grid, and clears grids whose field is down.</summary>
    private void UpdateWolfgateShieldRadar()
    {
        _wgRadarShielded.Clear();
        var shielded = EntityQueryEnumerator<ShipShieldedComponent>();
        while (shielded.MoveNext(out var grid, out var ship))
        {
            if (!IsWolfgateShieldLive(ship.Shield) || !TryComp<WGShipShieldVisualsComponent>(ship.Shield, out var visuals) ||
                !TryComp<PhysicsComponent>(ship.Shield, out var physics) || !physics.CanCollide)
                continue;

            _wgRadarShielded.Add(grid);
            var radar = EnsureComp<WGShipShieldRadarComponent>(grid);
            if (!WGShipShieldMesh.ContoursEqual(radar.Contours, visuals.Contours))
            {
                radar.Contours = visuals.Contours;
                DirtyField(grid, radar, nameof(WGShipShieldRadarComponent.Contours));
            }
            var health = MathF.Round(visuals.Health / RadarHealthStep) * RadarHealthStep;
            if (radar.Health != health)
            {
                radar.Health = health;
                DirtyField(grid, radar, nameof(WGShipShieldRadarComponent.Health));
            }
            var color = TryComp<ShipShieldVisualsComponent>(ship.Shield, out var look) ? look.ShieldColor : Color.White;
            if (radar.Color != color)
            {
                radar.Color = color;
                DirtyField(grid, radar, nameof(WGShipShieldRadarComponent.Color));
            }
        }

        var marked = EntityQueryEnumerator<WGShipShieldRadarComponent>();
        while (marked.MoveNext(out var grid, out _))
        {
            if (!_wgRadarShielded.Contains(grid))
                RemCompDeferred<WGShipShieldRadarComponent>(grid);
        }
    }
}
