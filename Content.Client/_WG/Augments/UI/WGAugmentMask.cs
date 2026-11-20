using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using Image = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WG.Augments.UI;

/// <summary>
/// The opaque pixels of a sprite state's south-facing first frame: their edges for an outline and the box around
/// them for an icon cropped to the sprite.
/// </summary>
internal sealed record WGAugmentMask(int Width, int Height, bool[] Opaque, List<(int X, int Y)> Pixels, List<UIBox2> Edges)
{
    /// <summary>Masks by RSI path and state; null if the state's image couldn't be read.</summary>
    private static readonly Dictionary<(ResPath, string), WGAugmentMask?> Cache = new();

    /// <summary>The box around the opaque pixels, in sprite pixels; empty if there are none.</summary>
    public UIBox2 Bounds { get; private set; }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Opaque[y * Width + x];

    /// <summary>The mask of a state in an RSI whose frames are <paramref name="size"/> pixels, or null if unreadable.</summary>
    public static WGAugmentMask? Of(IResourceManager resources, ResPath rsi, string? state, Vector2i size)
    {
        if (state == null)
            return null;
        if (Cache.TryGetValue((rsi, state), out var cached))
            return cached;

        WGAugmentMask? mask = null;
        try
        {
            using var stream = resources.ContentFileRead(rsi / $"{state}.png");
            using var image = Image.Load<Rgba32>(stream);
            var width = Math.Min(size.X, image.Width);
            var height = Math.Min(size.Y, image.Height);
            var pixels = image.GetPixelSpan();
            var opaque = new bool[width * height];
            var set = new List<(int, int)>();
            int left = width, top = height, right = 0, bottom = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * image.Width + x].A == 0)
                        continue;
                    opaque[y * width + x] = true;
                    set.Add((x, y));
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + 1);
                    bottom = Math.Max(bottom, y + 1);
                }
            }

            mask = new WGAugmentMask(width, height, opaque, set, new List<UIBox2>());
            if (set.Count > 0)
                mask.Bounds = new UIBox2(left, top, right, bottom);
            foreach (var (x, y) in set)
            {
                if (!mask.Contains(x - 1, y))
                    mask.Edges.Add(new UIBox2(x, y, x, y + 1));
                if (!mask.Contains(x + 1, y))
                    mask.Edges.Add(new UIBox2(x + 1, y, x + 1, y + 1));
                if (!mask.Contains(x, y - 1))
                    mask.Edges.Add(new UIBox2(x, y, x + 1, y));
                if (!mask.Contains(x, y + 1))
                    mask.Edges.Add(new UIBox2(x, y + 1, x + 1, y + 1));
            }
        }
        catch (Exception e)
        {
            Logger.GetSawmill("wg.augments").Warning($"Couldn't read {rsi}/{state}.png for the augment creator: {e.Message}");
        }

        Cache[(rsi, state)] = mask;
        return mask;
    }
}
