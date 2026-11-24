using Robust.Shared.Prototypes;

namespace Content.Server._WG.PdaStorage;

/// <summary>
/// A download in progress on one PDA. Lives on the loader entity and is not persisted:
/// closing the store or the PDA aborts it.
/// </summary>
[RegisterComponent]
public sealed partial class PdaDownloadComponent : Component
{
    /// <summary>The store program whose UI receives progress.</summary>
    public EntityUid StoreProgram;

    public string AppId = string.Empty;
    public EntProtoId Program = default!;
    public int Size;
    public float Duration;
    public float Elapsed;
    public float NextUpdate;
}
