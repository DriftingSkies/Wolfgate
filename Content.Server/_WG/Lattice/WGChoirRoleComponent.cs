using Content.Shared.Roles;

namespace Content.Server._WG.Lattice;

/// <summary>On a Choir player's mind role, so admin tools list them as an antagonist.</summary>
[RegisterComponent]
public sealed partial class WGChoirRoleComponent : BaseMindRoleComponent;

/// <summary>On a Choir member's PDA: red glyphs scratched into it, for anyone who looks.</summary>
[RegisterComponent]
public sealed partial class WGChoirMarkComponent : Component;
