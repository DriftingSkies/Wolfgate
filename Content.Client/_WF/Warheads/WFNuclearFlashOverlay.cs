using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._WF.Warheads;

/// <summary>
/// Whites out the screen of anyone within a detonation's flash range, fading over a few seconds.
/// </summary>
public sealed partial class WFNuclearFlashOverlay : Overlay
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    private readonly WFWarheadEffectsSystem _effects;
    private readonly SharedTransformSystem _transform;

    public WFNuclearFlashOverlay()
    {
        IoCManager.InjectDependencies(this);
        _effects = _entMan.System<WFWarheadEffectsSystem>();
        _transform = _entMan.System<SharedTransformSystem>();
        ZIndex = 300;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _effects.Active.Count > 0 && _player.LocalEntity != null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player)
            return;

        var pos = _transform.GetMapCoordinates(player);
        var strongest = 0f;

        foreach (var det in _effects.Active)
        {
            if (det.MapId != pos.MapId || det.Event.FlashRange <= 0f)
                continue;

            var distance = Vector2.Distance(pos.Position, det.Event.Position);
            if (distance >= det.Event.FlashRange)
                continue;

            var age = _effects.Age(det);
            var reach = MathF.Sqrt(1f - distance / det.Event.FlashRange);

            // Snaps on in a tenth of a second, then fades out.
            var envelope = age < 0.1f
                ? age / 0.1f
                : MathF.Exp(-(age - 0.1f) / (WFWarheadEffectsSystem.FlashFade * 0.35f));

            strongest = Math.Max(strongest, reach * envelope);
        }

        if (strongest <= 0.005f)
            return;

        // White first, warming to amber as it fades.
        var color = Color.InterpolateBetween(new Color(1f, 0.75f, 0.45f), Color.White, Math.Clamp(strongest * 1.4f, 0f, 1f));
        args.ScreenHandle.DrawRect(new UIBox2(Vector2.Zero, _clyde.ScreenSize),
            color.WithAlpha(Math.Clamp(strongest, 0f, 0.97f)));
    }
}
