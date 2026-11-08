namespace Content.Server._WF.Outposts.Components;

/// <summary>A hull a landing pad is guiding down to it or up to orbit, until its hop is over.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPadGuidedComponent : Component
{
    [ViewVariables]
    public EntityUid Pad;
}
