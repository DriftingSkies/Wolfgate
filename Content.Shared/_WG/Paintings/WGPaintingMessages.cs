using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.Paintings;

[Serializable, NetSerializable]
public enum WGCanvasUiKey : byte
{
    Key,
}

/// <summary>A framed painting seen up close; its state is a finished <see cref="WGCanvasUiState"/>.</summary>
[Serializable, NetSerializable]
public enum WGPaintingViewUiKey : byte
{
    Key,
}

/// <summary>The canvas as its painters see it.</summary>
[Serializable, NetSerializable]
public sealed class WGCanvasUiState(int width, int height, byte[] pixels, bool finished, string title, string artist)
    : BoundUserInterfaceState
{
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly byte[] Pixels = pixels;
    public readonly bool Finished = finished;
    public readonly string Title = title;
    public readonly string Artist = artist;
}

/// <summary>Paints pixels, given as row-major indices, with the held tool's colour, or erases them.</summary>
[Serializable, NetSerializable]
public sealed class WGCanvasPaintMessage(ushort[] pixels, bool erase) : BoundUserInterfaceMessage
{
    public readonly ushort[] Pixels = pixels;
    public readonly bool Erase = erase;
}

/// <summary>Sets the colour of the held paint palette.</summary>
[Serializable, NetSerializable]
public sealed class WGCanvasPickColorMessage(Color color) : BoundUserInterfaceMessage
{
    public readonly Color Color = color;
}

/// <summary>Saves a colour on the held paint palette, or removes a saved one.</summary>
[Serializable, NetSerializable]
public sealed class WGCanvasSwatchMessage(Color color, bool remove) : BoundUserInterfaceMessage
{
    public readonly Color Color = color;
    public readonly bool Remove = remove;
}

/// <summary>Finishes the painting under a title, signed with the painter's name or anonymously.</summary>
[Serializable, NetSerializable]
public sealed class WGCanvasFinishMessage(string title, bool anonymous) : BoundUserInterfaceMessage
{
    public readonly string Title = title;
    public readonly bool Anonymous = anonymous;
}

/// <summary>Asks for the pixels of catalogue paintings, by hash.</summary>
[Serializable, NetSerializable]
public sealed class WGPaintingArtRequestEvent(List<string> hashes) : EntityEventArgs
{
    public readonly List<string> Hashes = hashes;
}

/// <summary>A catalogue painting's pixels, or null pixels when there is no such painting.</summary>
[Serializable, NetSerializable]
public sealed class WGPaintingArtEvent(string hash, int width, int height, byte[]? pixels) : EntityEventArgs
{
    public readonly string Hash = hash;
    public readonly int Width = width;
    public readonly int Height = height;
    public readonly byte[]? Pixels = pixels;
}

/// <summary>One painting in the art app's list.</summary>
[Serializable, NetSerializable]
public sealed record WGArtAppEntry(string Hash, int Width, int Height, string Title, string Artist, bool Mine);

/// <summary>A page of the art app: the catalogue or the player's own paintings.</summary>
[Serializable, NetSerializable]
public sealed class WGArtAppUiState(
    List<WGArtAppEntry> entries,
    int page,
    int pages,
    bool mine,
    string search,
    int price,
    bool available) : BoundUserInterfaceState
{
    public readonly List<WGArtAppEntry> Entries = entries;
    public readonly int Page = page;
    public readonly int Pages = pages;
    public readonly bool Mine = mine;
    public readonly string Search = search;
    public readonly int Price = price;

    /// <summary>The catalogue has loaded from the database.</summary>
    public readonly bool Available = available;
}

[Serializable, NetSerializable]
public enum WGArtAppAction : byte
{
    Page,
    Search,
    ShowCatalogue,
    ShowMine,
    Order,
    Remove,
}

[Serializable, NetSerializable]
public sealed class WGArtAppMessage(WGArtAppAction action, int page = 0, string text = "") : CartridgeMessageEvent
{
    public readonly WGArtAppAction Action = action;
    public readonly int Page = page;

    /// <summary>The search text, or the painting's hash for Order and Remove.</summary>
    public readonly string Text = text;
}
