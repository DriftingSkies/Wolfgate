using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.PdaStorage;

/// <summary>
/// A removable data disk. Its programs live in the <see cref="ContainerId"/> container so they
/// travel with the disk item, including when it leaves the PDA or is saved.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class DataDiskComponent : Component
{
    public const string ContainerId = "data";

    /// <summary>Total storage in GQ.</summary>
    [DataField]
    public int Capacity = 32;

    /// <summary>Programs the disk ships with, installed into its container at map init.</summary>
    [DataField]
    public List<EntProtoId> StartingPrograms = new();
}
