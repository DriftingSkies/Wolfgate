using Content.Shared._WF.EngineCompat;

namespace Content.Server.NPC.Queries.Curves;

public sealed partial class PresetCurve : IUtilityCurve
{
    [DataField("preset", required: true, customTypeSerializer:typeof(PrototypeIdSerializer<UtilityCurvePresetPrototype>))] public  string Preset = default!;
}
