using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Traits.Conditions;

/// <summary>
/// Requires the character to spawn as a specific job.
/// </summary>
public sealed partial class HasJobCondition : BaseTraitCondition
{
    [DataField(required: true)]
    public ProtoId<JobPrototype> Job = string.Empty;

    protected override bool? EvaluateImplementation(TraitConditionContext ctx)
    {
        if (ctx.JobId == null)
            return ctx.Player == null ? null : false;

        return ctx.JobId == Job;
    }

    public override string GetTooltip(IPrototypeManager proto, ILocalizationManager loc)
    {
        var jobName = Job.Id;
        var jobColor = "#ffffff";

        if (proto.TryIndex(Job, out var jobProto))
        {
            jobName = loc.GetString(jobProto.Name);

            foreach (var department in proto.EnumeratePrototypes<DepartmentPrototype>())
            {
                if (!department.Roles.Contains(Job))
                    continue;

                jobColor = department.Color.ToHex();
                break;
            }
        }

        return Invert
            ? loc.GetString("trait-condition-job-not", ("job", jobName), ("color", jobColor))
            : loc.GetString("trait-condition-job-is", ("job", jobName), ("color", jobColor));
    }
}
