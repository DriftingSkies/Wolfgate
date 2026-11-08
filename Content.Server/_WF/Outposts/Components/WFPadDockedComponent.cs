namespace Content.Server._WF.Outposts.Components;

/// <summary>A hull parked on an outpost's landing pad: held static over the outpost until the pad lifts it to orbit.</summary>
[RegisterComponent, UnsavedComponent]
public sealed partial class WFPadDockedComponent : Component
{
    [ViewVariables]
    public EntityUid Pad;
}
