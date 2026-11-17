using Content.Shared._WG.Lattice;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WG.Lattice;

/// <summary>
/// Kyphrus's fall and burst over the whole view: as it falls in to a point everything goes black, and stays black until
/// the burst lights everything up in one blinding flash that fades into the dark.
/// </summary>
public sealed partial class WGTacetOverlay : Overlay
{
    [Dependency] private IEntityManager _ent = default!;
    [Dependency] private IEyeManager _eye = default!;

    /// <summary>Seconds before it has fallen in that the dark starts closing over the view.</summary>
    private const float Plunge = 0.6f;

    /// <summary>How fast the burst's flash fades, per second.</summary>
    private const float FlashFade = 0.9f;

    private static readonly Color Flash = Color.FromHex("#f6ecff");

    private Color _colour;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public WGTacetOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        var maps = _ent.System<SharedMapSystem>();
        var map = maps.GetMapOrInvalid(_eye.CurrentEye.Position.MapId);
        if (_ent.System<WGLatticeViewSystem>().ImplosionSeconds(map, out _) is not { } seconds
            || seconds < WGLatticeImplosion.Collapse - Plunge)
            return false;

        if (seconds < WGLatticeImplosion.Burst)
        {
            var dark = Math.Clamp((seconds - (WGLatticeImplosion.Collapse - Plunge)) / Plunge, 0f, 1f);
            _colour = Color.Black.WithAlpha(dark);
            return true;
        }

        var flash = MathF.Exp(-(seconds - WGLatticeImplosion.Burst) * FlashFade);
        _colour = Flash.WithAlpha(flash);
        return flash > 0.01f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        args.ScreenHandle.DrawRect(args.ViewportBounds, _colour);
    }
}
