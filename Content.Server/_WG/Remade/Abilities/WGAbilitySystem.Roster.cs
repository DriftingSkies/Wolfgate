using Content.Server._WG.Remade.Roster;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>The systems and helpers the roster's abilities (leaps, spits, bursts, shells) use.</summary>
public sealed partial class WGAbilitySystem
{
    [Dependency] private WGExploderSystem _exploder = default!;
    [Dependency] private WGRosterSystem _roster = default!;
    [Dependency] private WGShellSystem _shell = default!;

    public WGExploderSystem Exploder => _exploder;

    public WGRosterSystem Roster => _roster;

    public WGShellSystem Shell => _shell;

    /// <summary>Shakes the mob itself for a while, as if something is about to burst.</summary>
    public void Swell(EntityUid uid, float seconds)
    {
        _jitter.DoJitter(uid, TimeSpan.FromSeconds(seconds), true, 12f, 14f, true);
    }

    /// <summary>A random number between two values.</summary>
    public float RandomRange(float min, float max)
    {
        return _random.NextFloat(min, max);
    }

    /// <summary>The direction from the mob to another entity, in the world.</summary>
    public System.Numerics.Vector2 Toward(EntityUid from, EntityUid to)
    {
        return _transform.GetWorldPosition(to) - _transform.GetWorldPosition(from);
    }
}
