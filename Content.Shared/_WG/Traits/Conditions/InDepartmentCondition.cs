using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Traits.Conditions;

/// <summary>
/// Requires the character's job to belong to a specific department.
/// </summary>
public sealed partial class InDepartmentCondition : BaseTraitCondition
{
    [DataField(required: true)]
    public ProtoId<DepartmentPrototype> Department = string.Empty;

    protected override bool? EvaluateImplementation(TraitConditionContext ctx)
    {
        if (ctx.JobId == null)
            return ctx.Player == null ? null : false;

        if (ctx.Proto == null)
            return null;

        if (!ctx.Proto.TryIndex(Department, out var department))
            return false;

        return department.Roles.Contains(ctx.JobId);
    }

    public override string GetTooltip(IPrototypeManager proto, ILocalizationManager loc)
    {
        var departmentName = Department.Id;
        var departmentColor = "#ffffff";

        if (proto.TryIndex(Department, out var department))
        {
            departmentName = loc.GetString(department.Name);
            departmentColor = department.Color.ToHex();
        }

        return Invert
            ? loc.GetString("trait-condition-department-not", ("department", departmentName), ("color", departmentColor))
            : loc.GetString("trait-condition-department-is", ("department", departmentName), ("color", departmentColor));
    }
}
