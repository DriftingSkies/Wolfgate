namespace Content.Shared._Mono.Blocking.Components;

/// <summary>
/// Marks that this gun can be used alongside a shield. Note that this allows somebody to DOUBLE their effective health while using this gun, so do not add it willy-nilly.
/// </summary>
[RegisterComponent]
// WOLFGATE(Weapons) START: a gun that inherits the exemption can switch it off, as a child can't drop a component
// public sealed partial class CanShootWithShieldComponent : Component;
public sealed partial class CanShootWithShieldComponent : Component
{
    /// <summary>Whether the gun may be fired alongside a shield.</summary>
    [DataField]
    public bool Enabled = true;
}
// WOLFGATE END
