using System.Numerics;
using Content.Server.Projectiles;
using Content.Shared.Projectiles;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;

namespace Content.Server._WF.Weapons.Projectiles;

/// <summary>Keeps wide-circle shrapnel from hitting through walls.</summary>
public sealed class WFProximityShrapnelSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private ProjectileSystem _projectiles = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFProximityShrapnelComponent, PreventCollideEvent>(OnPreventCollide);
    }

    private void OnPreventCollide(Entity<WFProximityShrapnelComponent> ent, ref PreventCollideEvent args)
    {
        var other = args.OtherEntity;
        // What the sweep finds lies on the pellet's own path, however far a closing hull makes that this tick.
        if (args.Cancelled || _projectiles.WfSweptRound == ent.Owner || HasComp<ProjectileComponent>(other) ||
            Transform(other).GridUid == null)
            return;

        if (!TryComp<FixturesComponent>(ent, out var fixtures) ||
            !fixtures.Fixtures.TryGetValue(SharedProjectileSystem.ProjectileFixture, out var fixture) || fixture != args.OurFixture)
            return;

        var at = _transform.GetWorldPosition(ent);
        var box = _lookup.GetWorldAABB(other);
        var nearest = Vector2.Clamp(at, box.BottomLeft, box.TopRight);
        if ((nearest - at).LengthSquared() > ent.Comp.Reach * ent.Comp.Reach)
            args.Cancelled = true;
    }
}
