using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Scales the melee damage of <see cref="FaunaSwarmBonusComponent"/> mobs by the allies around them.
/// </summary>
public sealed partial class FaunaSwarmBonusSystem : EntitySystem
{
    [Dependency] private FaunaHelperSystem _helper = default!;

    private readonly HashSet<Entity<MobStateComponent>> _swarm = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaSwarmBonusComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
    }

    private void OnGetMeleeDamage(Entity<FaunaSwarmBonusComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (args.User != ent.Owner)
            return;

        _helper.GetKin(ent, ent.Comp.Radius, _swarm);
        var allies = _swarm.Count;
        if (allies > 0)
            args.Damage *= 1 + allies * ent.Comp.PerAlly;
    }
}
