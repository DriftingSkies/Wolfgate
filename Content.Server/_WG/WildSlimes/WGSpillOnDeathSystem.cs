using Content.Server.Fluids.EntitySystems;
using Content.Shared.Mobs;

namespace Content.Server._WG.WildSlimes;

/// <summary>Spills a mob's <see cref="WGSpillOnDeathComponent"/> solution where it dies.</summary>
public sealed class WGSpillOnDeathSystem : EntitySystem
{
    [Dependency] private PuddleSystem _puddle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGSpillOnDeathComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnMobStateChanged(Entity<WGSpillOnDeathComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || ent.Comp.Spilled)
            return;

        ent.Comp.Spilled = true;
        _puddle.TrySpillAt(ent.Owner, ent.Comp.Solution.Clone(), out _);
    }
}
