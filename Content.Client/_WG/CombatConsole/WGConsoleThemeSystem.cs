using Content.Client._WG.Stylesheets;
using Content.Shared._WG.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client._WG.CombatConsole;

/// <summary>Caches the active instrument palette for drawing without per-frame configuration lookups.</summary>
public sealed partial class WGConsoleThemeSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;

    public WolfgateSkin Skin { get; private set; } = WolfgateSkins.Default;

    public override void Initialize()
    {
        base.Initialize();
        _configuration.OnValueChanged(WolfgateCVars.UiStyle, Changed, true);
    }

    public override void Shutdown()
    {
        _configuration.UnsubValueChanged(WolfgateCVars.UiStyle, Changed);
        base.Shutdown();
    }

    private void Changed(string id) => Skin = WolfgateSkins.Get(id);
}
