using Robust.Client.Graphics;

namespace Content.Client.Interaction;

public sealed partial class DragDropSystem
{
    /// <summary>Whether a shader is one of the drop target highlights, so photos can leave it out.</summary>
    public bool IsDropTargetShader(ShaderInstance shader)
    {
        return shader == _dropTargetInRangeShader || shader == _dropTargetOutOfRangeShader;
    }
}
