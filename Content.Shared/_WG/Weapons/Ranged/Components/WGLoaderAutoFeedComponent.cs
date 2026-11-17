using Robust.Shared.GameStates;

namespace Content.Shared._WG.Weapons.Ranged.Components;

/// <summary>
/// Ship gun that refills itself from linked ammo loaders after each shot.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WGLoaderAutoFeedComponent : Component;
