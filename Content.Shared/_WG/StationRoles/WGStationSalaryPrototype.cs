using Content.Shared._NF.Bank.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.StationRoles;

/// <summary>The salary of a station job. The ID is the job's ID; jobs without one aren't paid.</summary>
[Prototype("wgStationSalary")]
public sealed partial class WGStationSalaryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Thalers per hour on duty.</summary>
    [DataField(required: true)]
    public int Wage;

    /// <summary>The sector account that pays it. Invalid means the faction has no account, so nothing is drawn.</summary>
    [DataField]
    public SectorBankAccount Account = SectorBankAccount.Invalid;
}
