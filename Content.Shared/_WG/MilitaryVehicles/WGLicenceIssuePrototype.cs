using Content.Shared.Roles;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.MilitaryVehicles;

/// <summary>Jobs whose players start the shift with a licence, made out to them.</summary>
[Prototype("wgLicenceIssue")]
public sealed partial class WGLicenceIssuePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public EntProtoId Licence;

    [DataField(required: true)]
    public List<ProtoId<JobPrototype>> Jobs = new();
}
