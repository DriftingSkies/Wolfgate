using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;

namespace Content.Shared._WG.Wolfmed.Life;

/// <summary>Slows a body whose heart has stopped while it is still on its feet; no sprinting with no pulse.</summary>
public sealed class WolfmedArrestCollapseSystem : EntitySystem
{
    /// <summary>Walk speed lost by the end of the collapse.</summary>
    private const float SlowdownAtCollapse = 0.4f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedCardiacArrestComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(Entity<WolfmedCardiacArrestComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        var walk = 1f - SlowdownAtCollapse * ent.Comp.CollapseFraction();
        var sprint = walk * MovementSpeedModifierComponent.DefaultBaseWalkSpeed /
                     MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
        args.ModifySpeed(walk, sprint);
    }
}
