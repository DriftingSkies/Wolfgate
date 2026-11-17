using Robust.Client.Graphics;

namespace Content.Client.Interactable.Components;

public sealed partial class InteractionOutlineComponent
{
    /// <summary>The hover outline shader while the mouse is over this entity, so photos can leave it out.</summary>
    public ShaderInstance? HoverShader => _shader;
}
