using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Elites;
using Content.Server.Beam;
using Robust.Shared.Prototypes;

namespace Content.Server._WG.Remade.Abilities;

/// <summary>The helpers the elite abilities (rams, slams, blinks, grabs, latches, taunts) use.</summary>
public sealed partial class WGAbilitySystem
{
    [Dependency] private WGEliteSystem _elite = default!;
    [Dependency] private BeamSystem _beam = default!;
    [Dependency] private WGTauntSystem _taunt = default!;

    public WGEliteSystem Elite => _elite;

    public WGTauntSystem Taunt => _taunt;

    /// <summary>The living Remade within range of the mob, not counting itself.</summary>
    public List<EntityUid> Allies(EntityUid around, float range)
    {
        var result = new List<EntityUid>();
        _mobs.Clear();
        _lookup.GetEntitiesInRange(Transform(around).Coordinates, range, _mobs);
        foreach (var mob in _mobs)
        {
            if (mob.Owner != around && HasComp<WGRemadeComponent>(mob) && !IsDead(mob))
                result.Add(mob);
        }

        return result;
    }

    /// <summary>Draws a beam between two entities, such as a tongue.</summary>
    public void DrawBeam(EntityUid from, EntityUid to, EntProtoId beam)
    {
        _beam.TryCreateBeam(from, to, beam.Id);
    }
}
