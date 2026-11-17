using System.Numerics;
using Content.Shared._WG.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.ShipShields;

/// <summary>Tracks short-lived impact waves on hull-shaped shield surfaces.</summary>
public sealed partial class WGShipShieldOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<EntityUid, List<Impact>> _impacts = new();
    private readonly List<EntityUid> _expired = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGShipShieldImpactEvent>(OnImpact);
        _overlays.AddOverlay(new WGShipShieldOverlay(EntityManager, _prototypes, _timing, _impacts));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WGShipShieldOverlay>();
        _impacts.Clear();
        base.Shutdown();
    }

    private void OnImpact(WGShipShieldImpactEvent ev)
    {
        if (!TryGetEntity(ev.Shield, out var shield) || shield == null ||
            !float.IsFinite(ev.Position.X) || !float.IsFinite(ev.Position.Y) || !float.IsFinite(ev.Strength) || ev.Strength <= 0f)
            return;

        if (!_impacts.TryGetValue(shield.Value, out var impacts))
        {
            if (_impacts.Count >= 128)
                return;
            impacts = new List<Impact>();
            _impacts.Add(shield.Value, impacts);
        }

        if (impacts.Count >= 32)
            impacts.RemoveAt(0);
        impacts.Add(new Impact(ev.Position, _timing.CurTime, Math.Clamp(ev.Strength, 0f, 1f)));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _expired.Clear();
        foreach (var (uid, impacts) in _impacts)
        {
            impacts.RemoveAll(impact => (_timing.CurTime - impact.Time).TotalSeconds >= WGShipShieldEffects.HeatLifetime);
            if (Deleted(uid) || impacts.Count == 0)
                _expired.Add(uid);
        }
        foreach (var uid in _expired)
            _impacts.Remove(uid);
    }

    /// <summary>A bounded visual impact in shield-local coordinates.</summary>
    public readonly record struct Impact(Vector2 Position, TimeSpan Time, float Strength);
}
