using Robust.Client.Graphics;

namespace Content.Client.Outline;

public sealed partial class TargetOutlineSystem
{
    /// <summary>Whether a shader is one of the targeting outlines, so photos can leave it out.</summary>
    public bool IsTargetShader(ShaderInstance shader)
    {
        return shader == _shaderTargetValid || shader == _shaderTargetInvalid;
    }
}
