// WOLFGATE(Xenoborgs) START: wizden#45247 guided missile targeting, ported whole; logic in the SharedGunSystem/GunSystem TargetFinder partials.
using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Makes so when this hitscan hits something, it marks them as the target for the gun that shot the hitscan
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TargetFinderHitscanComponent : Component;
// WOLFGATE END
