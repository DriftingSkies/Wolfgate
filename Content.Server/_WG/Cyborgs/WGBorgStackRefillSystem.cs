using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Server.Stack;
using Content.Shared._WG.Cyborgs;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Stacks;
using Robust.Shared.Timing;

namespace Content.Server._WG.Cyborgs;

/// <summary>
/// Restocks <see cref="WGBorgStackRefillComponent"/> stacks one unit at a time, paid for out of their cyborg's cell.
/// </summary>
public sealed partial class WGBorgStackRefillSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private PowerCellSystem _powerCell = default!;
    [Dependency] private StackSystem _stack = default!;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>Share of the cell a refill never spends, so restocking can't run the cyborg flat.</summary>
    public const float Reserve = 0.25f;

    private TimeSpan _next;

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _next)
            return;

        _next = _timing.CurTime + Interval;
        RefillAll();
    }

    /// <summary>
    /// Adds one unit to every refilling stack that is short and aboard a cyborg whose cell can pay for it.
    /// </summary>
    public void RefillAll()
    {
        var query = EntityQueryEnumerator<WGBorgStackRefillComponent, StackComponent>();
        while (query.MoveNext(out var uid, out var refill, out var stack))
        {
            if (stack.Count >= refill.Target || GetChassis(uid) is not { } chassis
                || !_powerCell.TryGetBatteryFromSlot(chassis, out var cell, out var battery)
                || battery.CurrentCharge - refill.ChargePerUnit < battery.MaxCharge * Reserve
                || !_battery.TryUseCharge(cell.Value, refill.ChargePerUnit, battery))
                continue;

            _stack.SetCount(uid, stack.Count + 1, stack);
        }
    }

    /// <summary>
    /// The cyborg holding the stack, or carrying it stowed in one of its modules.
    /// </summary>
    private EntityUid? GetChassis(EntityUid stack)
    {
        // In hand the stack's parent is the chassis; stowed, it is the module, whose parent is the chassis.
        var parent = Transform(stack).ParentUid;
        if (HasComp<BorgChassisComponent>(parent))
            return parent;

        if (!parent.IsValid())
            return null;

        var chassis = Transform(parent).ParentUid;
        return HasComp<BorgChassisComponent>(chassis) ? chassis : null;
    }
}
