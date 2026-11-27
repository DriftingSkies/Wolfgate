using Content.Shared.Access;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.PdaStorage.Prototypes;

/// <summary>Devices an application can be installed on.</summary>
[Flags]
public enum PdaDeviceFlags : byte
{
    None = 0,
    Pda = 1 << 0,
}

/// <summary>
/// A downloadable PDA application. The program itself is an ordinary cartridge entity prototype;
/// this prototype only describes how it is listed, gated and ordered in the store.
/// Size comes from the program's <see cref="Content.Shared.CartridgeLoader.CartridgeComponent.Size"/>.
/// </summary>
[Prototype("pdaApp")]
public sealed partial class PdaAppPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>The program entity spawned on install. Also determines the display name and size.</summary>
    [DataField(required: true)]
    public EntProtoId Program;

    /// <summary>Store blurb shown under the app name.</summary>
    [DataField(required: true)]
    public LocId Description;

    [DataField(required: true)]
    public ProtoId<PdaAppCategoryPrototype> Category;

    /// <summary>Devices that may install and run the app.</summary>
    [DataField]
    public PdaDeviceFlags Devices = PdaDeviceFlags.Pda;

    /// <summary>Access tags required to download. Any one match is enough; empty means public.</summary>
    [DataField]
    public List<ProtoId<AccessLevelPrototype>> DownloadAccess = new();

    [DataField]
    public bool AvailableInStore = true;

    /// <summary>Whether the listing is shown as an official source.</summary>
    [DataField]
    public bool Verified = true;

    /// <summary>Listing order inside a category; lower comes first.</summary>
    [DataField]
    public int SortOrder;
}
