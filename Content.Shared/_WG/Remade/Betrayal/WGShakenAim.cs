using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameStates;

namespace Content.Shared._WG.Remade.Betrayal;

/// <summary>A hand that won't hold still: every gun the holder shoots spreads wider by this much.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGShakenAimComponent : Component
{
    /// <summary>Multiplies the gun's spread. 1 is steady.</summary>
    [DataField, AutoNetworkedField]
    public float Spread = 1f;

    /// <summary>The widest a shaking hand throws a shot, in degrees.</summary>
    [DataField]
    public float MaxAngle = 30f;
}

/// <summary>Widens the spread of the guns a shaken player holds. Shared, so the client predicts the same shots.</summary>
public sealed class WGShakenAimSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WGShakenAimComponent, GunRefreshModifiersEvent>(OnRefresh);
    }

    private void OnRefresh(Entity<WGShakenAimComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var limit = MathHelper.DegreesToRadians(ent.Comp.MaxAngle);
        args.MinAngle = Math.Clamp(args.MinAngle * ent.Comp.Spread, 0f, limit);
        args.MaxAngle = Math.Clamp(args.MaxAngle * ent.Comp.Spread, 0f, limit);
        args.AngleIncrease *= ent.Comp.Spread;
    }
}
