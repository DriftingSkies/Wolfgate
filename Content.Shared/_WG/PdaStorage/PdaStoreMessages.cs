using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._WG.PdaStorage;

/// <summary>What the client asked the app store to do.</summary>
public enum PdaStoreAction : byte
{
    RequestState,
    Install,
    InstallFromMedia,
    Uninstall,
    CopyToDisk,
    CopyFromDisk,
}

/// <summary>Server-side reason a store action failed; the client localises it.</summary>
public enum PdaStoreError : byte
{
    None,
    UnknownApp,
    Unavailable,
    Incompatible,
    NoAccess,
    AlreadyInstalled,
    NoSpace,
    AlreadyDownloading,
    InstallFailed,
    NotInstalled,
    Readonly,
    NoDisk,
    DiskFull,
    InvalidMedia,
}

/// <summary>One action from the store UI. App ids and entity ids are always re-validated on the server.</summary>
[Serializable, NetSerializable]
public sealed class PdaStoreMessage : CartridgeMessageEvent
{
    public PdaStoreAction Action;
    public string AppId = string.Empty;
    public NetEntity Program = NetEntity.Invalid;

    public PdaStoreMessage() { }

    public PdaStoreMessage(PdaStoreAction action, string appId = "", NetEntity program = default)
    {
        Action = action;
        AppId = appId;
        Program = program;
    }
}

/// <summary>A catalogue row with the flags computed for the PDA it is shown on.</summary>
[Serializable, NetSerializable]
public struct PdaStoreAppEntry
{
    public string Id;
    public string Name;
    public string Description;
    public string Category;
    public string CategoryName;
    public SpriteSpecifier? Icon;
    public int Size;
    public bool Installed;
    public bool Compatible;
    public bool HasAccess;
    public bool Verified;
}

/// <summary>A program already on a storage device (PDA drive, data disk or cartridge media).</summary>
[Serializable, NetSerializable]
public struct PdaStoreProgramEntry
{
    public NetEntity Program;
    public string AppId;
    public string Name;
    public int Size;
    public bool Readonly;
    public bool Running;
}

/// <summary>Everything the store screen needs for one render.</summary>
[Serializable, NetSerializable]
public sealed class PdaStoreUiState : BoundUserInterfaceState
{
    public int Capacity;
    public int Used;

    public bool Downloading;
    public string DownloadAppId = string.Empty;
    public int DownloadSize;
    public float DownloadProgress;

    public PdaStoreError Error;

    public List<PdaStoreAppEntry> Apps = new();
    public List<PdaStoreProgramEntry> Installed = new();

    // Removable media (Stage 5).
    public List<PdaStoreProgramEntry> Disk = new();
    public int DiskCapacity;
    public int DiskUsed;
    public string? DiskName;

    // Inserted cartridge, treated as single-app media.
    public List<PdaStoreProgramEntry> Cartridge = new();
}
