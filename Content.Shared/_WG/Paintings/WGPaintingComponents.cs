using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Paintings;

/// <summary>A canvas that can be painted pixel by pixel, then finished, framed and scanned into the art catalogue.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class WGCanvasComponent : Component
{
    [DataField(required: true)]
    public int Width;

    [DataField(required: true)]
    public int Height;

    /// <summary>The pixels as base64 RGBA, so saved canvases keep their painting. Empty for a blank canvas.</summary>
    [DataField]
    public string Data = string.Empty;

    /// <summary>The pixels, decoded from <see cref="Data"/> on the server. Clients only get <see cref="Art"/>.</summary>
    [ViewVariables]
    public byte[] Pixels = Array.Empty<byte>();

    /// <summary>Something has been painted, which shows the wip splotch until the painting is finished.</summary>
    [DataField, AutoNetworkedField]
    public bool Used;

    /// <summary>Finished: it can't be painted any more, and can be framed and scanned.</summary>
    [DataField, AutoNetworkedField]
    public bool Finished;

    [DataField, AutoNetworkedField]
    public string Title = string.Empty;

    /// <summary>The signature, a character name or anonymous.</summary>
    [DataField, AutoNetworkedField]
    public string Artist = string.Empty;

    /// <summary>The player who finished it, credited when it is scanned.</summary>
    [DataField]
    public Guid? AuthorUserId;

    /// <summary>Hash of the finished pixels, which names the painting in the catalogue.</summary>
    [DataField, AutoNetworkedField]
    public string Hash = string.Empty;

    /// <summary>A print ordered from the catalogue rather than an original.</summary>
    [DataField, AutoNetworkedField]
    public bool Print;

    /// <summary>The finished pixels, sent to clients to draw the painting.</summary>
    [AutoNetworkedField]
    public byte[]? Art;
}

/// <summary>A paint palette: paints any colour, keeps colours the painter saves on it and the ones used last.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WGPaintPaletteComponent : Component
{
    [DataField, AutoNetworkedField]
    public Color Selected = Color.Black;

    /// <summary>Colours the painter saved, in the order they were added.</summary>
    [DataField, AutoNetworkedField]
    public List<Color> Swatches = new();

    /// <summary>Colours painted with before the selected one, newest first.</summary>
    [DataField, AutoNetworkedField]
    public List<Color> Recent = new();
}

/// <summary>A wall frame for a finished canvas. A gallery frame hangs a random catalogue painting at round start.</summary>
[RegisterComponent]
public sealed partial class WGPaintingFrameComponent : Component
{
    public const string ContainerId = "painting";

    /// <summary>Takes the two large canvas sizes instead of the small ones.</summary>
    [DataField]
    public bool Large;

    [DataField]
    public bool Gallery;
}

/// <summary>An easel: holds a canvas up while it is painted.</summary>
[RegisterComponent]
public sealed partial class WGEaselComponent : Component
{
    public const string SlotId = "canvas";
}

[Serializable, NetSerializable]
public enum WGPaintingVisualLayers : byte
{
    Canvas,
    Art,
    Wip,
    Frame,
}
