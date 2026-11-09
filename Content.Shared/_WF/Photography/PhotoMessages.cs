using System.Numerics;
using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Photography;

/// <summary>Server to photographer: render this square of tiles and send it back.</summary>
[Serializable, NetSerializable]
public sealed class PhotoCaptureRequestEvent : EntityEventArgs
{
    public int RequestId;

    /// <summary>The map the photo is taken on.</summary>
    public NetEntity Map;

    /// <summary>The grid whose tiles the square follows, or invalid for none.</summary>
    public NetEntity Grid;

    /// <summary>Centre of the square in map coordinates.</summary>
    public Vector2 Centre;

    /// <summary>World rotation of the square's up direction.</summary>
    public float Rotation;

    /// <summary>Tiles a side.</summary>
    public int Tiles;

    /// <summary>Which tiles the photographer can see, row by row from the top left.</summary>
    public bool[] Visible = Array.Empty<bool>();

    public bool Monochrome;

    /// <summary>Ghosts to draw although the photographer can't normally see them.</summary>
    public NetEntity[] Ghosts = Array.Empty<NetEntity>();

    /// <summary>Cloaked entities to leave out, as the photographer can't see them.</summary>
    public NetEntity[] Hidden = Array.Empty<NetEntity>();
}

/// <summary>Client to server: the rendered photo for a request, as PNG.</summary>
[Serializable, NetSerializable]
public sealed class PhotoCaptureResultEvent(int requestId, byte[] png) : EntityEventArgs
{
    public readonly int RequestId = requestId;
    public readonly byte[] Png = png;
}

/// <summary>Client to server: send the image stored under a hash.</summary>
[Serializable, NetSerializable]
public sealed class PhotoImageRequestEvent(string hash) : EntityEventArgs
{
    public readonly string Hash = hash;
}

/// <summary>Server to client: a photo image as PNG, or null when the server has none under that hash.</summary>
[Serializable, NetSerializable]
public sealed class PhotoImageEvent(string hash, byte[]? png) : EntityEventArgs
{
    public readonly string Hash = hash;
    public readonly byte[]? Png = png;
}

/// <summary>Gallery window: print the photo at this index.</summary>
[Serializable, NetSerializable]
public sealed class PhotoGalleryPrintMessage(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

/// <summary>Gallery window: forget the photo at this index.</summary>
[Serializable, NetSerializable]
public sealed class PhotoGalleryDeleteMessage(int index) : BoundUserInterfaceMessage
{
    public readonly int Index = index;
}

/// <summary>Station AI action: take a photo of the clicked spot.</summary>
public sealed partial class PhotoAiCaptureActionEvent : WorldTargetActionEvent;

/// <summary>Station AI action: open the photo gallery.</summary>
public sealed partial class PhotoGalleryActionEvent : InstantActionEvent;
