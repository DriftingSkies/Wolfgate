using System.Numerics;
using Content.Shared._WF.ShipShields;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Content.Shared._WF.Remade.Ward;
using Content.Shared.CCVar;

namespace Content.Client._WF.Remade.Ward;

/// <summary>A hit on the stone's field, in the field's local frame.</summary>
public readonly record struct WFWardImpact(Vector2 Position, TimeSpan Time, float Strength);

/// <summary>
/// Draws the Ward's field its own way: a dark red veined membrane that pulses, with Marker glyphs drifting through it, which
/// brightens and ripples where it's hit and sets hard and bright at the Crescendo. It follows the ship shield's own hits, and the
/// ship shield overlay leaves these fields to it.
/// </summary>
public sealed partial class WFWardOverlaySystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;

    private const int MaxImpacts = 24;
    private const int MaxFields = 32;

    private readonly Dictionary<EntityUid, List<WFWardImpact>> _impacts = new();
    private readonly List<EntityUid> _expired = new();
    private WFWardOverlay _membrane = default!;
    private WFWardGlyphOverlay _glyphs = default!;

    /// <summary>Hits being remembered for fields. Tests read it.</summary>
    public int Tracked => _impacts.Count;

    public override void Initialize()
    {
        base.Initialize();
        _membrane = new WFWardOverlay(EntityManager, _prototypes, _timing, _impacts);
        _glyphs = new WFWardGlyphOverlay(EntityManager, _timing, GlyphFont());
        SubscribeNetworkEvent<WFShipShieldImpactEvent>(OnImpact);
        Subs.CVar(_config, CCVars.ReducedMotion, motion => _membrane.Still = _glyphs.Still = motion, true);
        _overlays.AddOverlay(_membrane);
        _overlays.AddOverlay(_glyphs);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlays.RemoveOverlay(_membrane);
        _overlays.RemoveOverlay(_glyphs);
        _impacts.Clear();
    }

    private Font GlyphFont() => WFMonolithTheme.Glyphs(_resources, WFWardGlyphOverlay.FontSize);

    private void OnImpact(WFShipShieldImpactEvent ev)
    {
        if (!TryGetEntity(ev.Shield, out var shield) || !HasComp<WFWardFieldComponent>(shield)
            || !float.IsFinite(ev.Position.X) || !float.IsFinite(ev.Position.Y) || !float.IsFinite(ev.Strength) || ev.Strength <= 0f)
        {
            return;
        }

        if (!_impacts.TryGetValue(shield.Value, out var list))
        {
            if (_impacts.Count >= MaxFields)
                return;

            list = new List<WFWardImpact>();
            _impacts.Add(shield.Value, list);
        }

        if (list.Count >= MaxImpacts)
            list.RemoveAt(0);

        list.Add(new WFWardImpact(ev.Position, _timing.CurTime, Math.Clamp(ev.Strength, 0f, 1f)));
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        _expired.Clear();
        foreach (var (uid, list) in _impacts)
        {
            list.RemoveAll(hit => (_timing.CurTime - hit.Time).TotalSeconds >= WFShipShieldEffects.HeatLifetime);
            if (list.Count == 0 || Deleted(uid))
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            _impacts.Remove(uid);
        }
    }
}
