using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Remade.Signals;

/// <summary>The stone's sight: the screen drained of colour and closed in at the edges in dark red. It uses the dread shader at a fixed strength.</summary>
public sealed partial class WFSignalOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "WFRemadeDread";

    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    private readonly ShaderInstance _shader;

    public WFSignalOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _proto.Index(ShaderId).InstanceUnique();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        return _entMan.TryGetComponent(_player.LocalEntity, out EyeComponent? eye) && args.Viewport.Eye == eye.Eye;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("wobble", 0f);
        _shader.SetParameter("split", 0.15f);
        _shader.SetParameter("vignette", 0.9f);
        _shader.SetParameter("drain", 0.75f);

        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }
}
