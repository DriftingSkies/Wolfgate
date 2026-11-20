using Content.Shared._WG.DarkFlash;
using Robust.Client.Graphics;
using Robust.Shared.Configuration;

namespace Content.Client.Flash;

public sealed partial class FlashOverlay
{
    [Dependency] private IConfigurationManager _wgCfg = default!;

    private ShaderInstance? _wgStillShader;

    /// <summary>The still fade when the dark flash option is on, otherwise the usual shader.</summary>
    private ShaderInstance WgFlashShader()
    {
        if (!_wgCfg.GetCVar(DarkFlashCVars.Enabled))
            return _shader;

        return _wgStillShader ??= _prototypeManager.Index<ShaderPrototype>("WGFlashedEffectStill").InstanceUnique();
    }
}
