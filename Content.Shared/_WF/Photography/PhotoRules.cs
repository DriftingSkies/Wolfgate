namespace Content.Shared._WF.Photography;

/// <summary>Limits shared by the camera, the capture upload and the photo text fields.</summary>
public static class PhotoRules
{
    /// <summary>Pixels per tile in a photo, the same as a sprite tile.</summary>
    public const int TilePixels = 32;

    /// <summary>Smallest and largest half-width setting. A setting of n takes 2n-1 tiles a side.</summary>
    public const int MinAperture = 1;
    public const int MaxAperture = 4;

    /// <summary>Largest PNG a client may upload for one photo.</summary>
    public const int MaxPngBytes = 256 * 1024;

    /// <summary>Side of the thumbnail drawn on photo and frame sprites.</summary>
    public const int ThumbnailPixels = 8;

    /// <summary>Bytes in a thumbnail: RGB for each pixel.</summary>
    public const int ThumbnailBytes = ThumbnailPixels * ThumbnailPixels * 3;

    /// <summary>Longest photo name, description and back text.</summary>
    public const int MaxNameLength = 32;
    public const int MaxDescriptionLength = 128;
    public const int MaxBackTextLength = 256;

    /// <summary>How many tiles a side an aperture setting takes.</summary>
    public static int TilesForAperture(int aperture)
    {
        return 2 * Math.Clamp(aperture, MinAperture, MaxAperture) - 1;
    }

    /// <summary>The next setting when cycling, wrapping round to the smallest.</summary>
    public static int NextAperture(int aperture)
    {
        return aperture >= MaxAperture ? MinAperture : aperture + 1;
    }
}
