using Content.Shared._Mono.Emp;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Containers;

namespace Content.Shared._WF.Xenoborgs;

/// <summary>
/// Lends a borg chassis's <see cref="EmpResistanceComponent"/> to the power cell in its slot, like wizden's cell EMP
/// relay, so a resistant chassis also shields its cell. EMP drains the cell directly.
/// </summary>
public sealed partial class CellHolderEmpResistanceSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PowerCellComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<PowerCellComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnInserted(Entity<PowerCellComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        var holder = args.Container.Owner;
        if (!HasComp<BorgChassisComponent>(holder) ||
            !HasComp<PowerCellSlotComponent>(holder) ||
            !TryComp<EmpResistanceComponent>(holder, out var holderResistance))
            return;

        if (TryComp<EmpResistanceComponent>(ent, out var own))
        {
            if (own.Coefficient <= holderResistance.Coefficient)
                return;

            EnsureComp<LentEmpResistanceComponent>(ent).OwnCoefficient = own.Coefficient;
            own.Coefficient = holderResistance.Coefficient;
            return;
        }

        AddComp(ent, new EmpResistanceComponent { Coefficient = holderResistance.Coefficient });
        EnsureComp<LentEmpResistanceComponent>(ent);
    }

    private void OnRemoved(Entity<PowerCellComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (!TryComp<LentEmpResistanceComponent>(ent, out var lent))
            return;

        if (lent.OwnCoefficient is { } own && TryComp<EmpResistanceComponent>(ent, out var resistance))
            resistance.Coefficient = own;
        else
            RemComp<EmpResistanceComponent>(ent);

        RemComp<LentEmpResistanceComponent>(ent);
    }
}

/// <summary>
/// Marks a cell whose <see cref="EmpResistanceComponent"/> was lent by its holder, to take back when the cell leaves.
/// </summary>
[RegisterComponent]
public sealed partial class LentEmpResistanceComponent : Component
{
    /// <summary>
    /// The cell's own coefficient before the loan, or null if it had no resistance.
    /// </summary>
    [ViewVariables]
    public float? OwnCoefficient;
}
