using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Remade.Dread;

/// <summary>Warps the player's screen the way the stone warps their head: wobble, colour split, closing dark and drained colour.</summary>
public sealed partial class WFDreadOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "WFRemadeDread";

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    /// <summary>Each part of the warp, from 0 to 1.</summary>
    public float Wobble;
    public float Split;
    public float Vignette;
    public float Drain;

    public WFDreadOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(ShaderId).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (!_entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) || args.Viewport.Eye != eye.Eye)
            return false;

        return Wobble + Split + Vignette + Drain > 0.001f;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("wobble", Wobble);
        _shader.SetParameter("split", Split);
        _shader.SetParameter("vignette", Vignette);
        _shader.SetParameter("drain", Drain);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
