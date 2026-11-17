namespace Content.Shared._WG.Traits;

/// <summary>Marks the Clown trait. Anything that depends on the trait checks for this component.</summary>
[RegisterComponent]
public sealed partial class ClownComponent : Component
{
    /// <summary>Client <c>font</c> prototype the clown's speech is drawn in, over the font picked in their profile.</summary>
    [DataField]
    public string Font = "ComicNeue";
}
