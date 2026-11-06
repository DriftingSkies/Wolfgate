using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Robust.Shared.Physics.Events;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets <see cref="FaunaFactionPassComponent"/> projectiles pass through allied mobs.
/// </summary>
public sealed partial class FaunaFactionPassSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _faction = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FaunaFactionPassComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<FaunaFactionPassComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        var other = args.OtherEntity;
        if (ent.Comp.Factions.Count > 0)
        {
            if (_faction.IsMemberOfAny(other, ent.Comp.Factions))
                args.Cancelled = true;

            return;
        }

        if (TryComp<ProjectileComponent>(ent, out var projectile) &&
            projectile.Shooter is { } shooter &&
            shooter != other &&
            _faction.IsEntityFriendly(shooter, other))
            args.Cancelled = true;
    }
}
