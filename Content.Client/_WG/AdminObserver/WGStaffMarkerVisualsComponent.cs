using Robust.Client.Graphics;

namespace Content.Client._WG.AdminObserver;

/// <summary>What this client has made to draw one staff marker: its shaders and its aura light.</summary>
[RegisterComponent]
public sealed partial class WGStaffMarkerVisualsComponent : Component
{
    /// <summary>The hologram pass over the entity's sprite.</summary>
    public ShaderInstance? Hologram;

    /// <summary>The aura outline pass over the entity's sprite.</summary>
    public ShaderInstance? Outline;

    /// <summary>The shield skin the overlay draws.</summary>
    public ShaderInstance? Skin;

    /// <summary>The aura's light, an entity only this client has.</summary>
    public EntityUid? Light;
}
