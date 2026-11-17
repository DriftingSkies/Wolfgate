using Content.Server._WG.Remade.Critters;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>What the critters' habits give the abilities.</summary>
public sealed partial class WGAbilitySystem
{
    [Dependency] private WGWallClingSystem _cling = default!;

    /// <summary>Whether the mob is standing against a wall, where a leaper leaps harder.</summary>
    public bool AgainstWall(EntityUid uid)
    {
        return _cling.AgainstWall(uid);
    }
}
