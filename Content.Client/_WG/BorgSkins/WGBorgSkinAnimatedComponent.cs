namespace Content.Client._WG.BorgSkins;

/// <summary>Marks a borg with a Wolfgate skin, so <see cref="WGBorgSkinSystem"/> poses and animates it each frame.</summary>
[RegisterComponent]
public sealed partial class WGBorgSkinAnimatedComponent : Component
{
    /// <summary>Swap the chassis layers to their <c>_moving</c> states while the borg moves.</summary>
    [ViewVariables]
    public bool Walks;

    /// <summary>The body layer is showing a pose state, to be undone when the pose ends.</summary>
    [ViewVariables]
    public bool Posed;
}
