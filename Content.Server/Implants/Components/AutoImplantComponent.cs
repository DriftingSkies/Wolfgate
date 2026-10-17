using Robust.Shared.Prototypes;
using Content.Shared._WF.EngineCompat;

namespace Content.Server.Implants.Components;

/// <summary>
/// Implants an entity automatically on MapInit.
/// </summary>
[RegisterComponent]
public sealed partial class AutoImplantComponent : Component
{
    /// <summary>
    /// List of implants to inject.
    /// </summary>
    [DataField("implants", required: true, customTypeSerializer: typeof(PrototypeIdListSerializer<EntityPrototype>))]
    public List<string> Implants = new();
}
