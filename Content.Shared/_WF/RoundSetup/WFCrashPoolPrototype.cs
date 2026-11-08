using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.RoundSetup;

/// <summary>The hulls a Crashlanded crew may wake up aboard, one picked at random per crash.</summary>
[Prototype("wfCrashPool")]
public sealed partial class WFCrashPoolPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Civilian vessels with enclosed crew spaces and room for a full crew of survivors.</summary>
    [DataField(required: true)]
    public List<ProtoId<VesselPrototype>> Vessels = new();
}
