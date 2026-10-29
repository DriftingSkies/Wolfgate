namespace Content.Shared._CE.ZLevels.Core.Components;

public sealed partial class CEZPhysicsComponent
{
    /// <summary>
    /// Client: whether last frame's sprite offset carries a height. Entities at height 0 with none skip the per-frame
    /// offset passes, which otherwise touch every item, mob and structure in view.
    /// </summary>
    [ViewVariables]
    public bool WFHeightOffset;
}
