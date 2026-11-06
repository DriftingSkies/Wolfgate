using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.Damage;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Spreads a retaliation of a <see cref="FaunaGroupRetaliateComponent"/> mob to its kin in range.
/// </summary>
public sealed partial class FaunaGroupRetaliateSystem : EntitySystem
{
    [Dependency] private NPCRetaliationSystem _retaliation = default!;
    [Dependency] private FaunaHelperSystem _helper = default!;

    private readonly HashSet<Entity<NPCRetaliationComponent>> _kin = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaGroupRetaliateComponent, DamageChangedEvent>(OnDamageChanged);
    }

    private void OnDamageChanged(Entity<FaunaGroupRetaliateComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin || origin == ent.Owner)
            return;

        _helper.GetKin(ent, ent.Comp.Radius, _kin);
        foreach (var mate in _kin)
        {
            _retaliation.TryRetaliate(mate, origin);
        }
    }
}
