namespace Content.Server._WG.Warheads.Components;

/// <summary>
/// A warhead that only arms with its own key in its keyhole: no timer start, no safety off and no key turn without it.
/// </summary>
[RegisterComponent]
public sealed partial class WGWarheadKeyLockComponent : Component
{
    /// <summary>The item slot that holds the key.</summary>
    [DataField]
    public string Slot = "key";
}

/// <summary>A physical warhead key, cut for one warhead.</summary>
[RegisterComponent]
public sealed partial class WGNukeKeyComponent : Component
{
    /// <summary>The warhead it arms; null fits none.</summary>
    [DataField]
    public EntityUid? Warhead;
}
