using System.IO;
using System.Linq;
using Content.Shared._WF.Photography;
using Content.Shared.GameTicking;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.Photography;

/// <summary>
/// Downloads full photo images by hash for the viewers, and builds sprite thumbnails from the data each photo carries.
/// </summary>
public sealed partial class PhotoImageSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long to wait before asking again, as the server skips requests over its rate limit.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    private readonly Dictionary<string, Texture> _textures = new();

    private readonly Dictionary<string, Texture> _thumbnails = new();

    /// <summary>Images asked for and not answered yet, with when they were last asked for.</summary>
    private readonly Dictionary<string, TimeSpan> _requested = new();

    /// <summary>Images the server has none for, or that didn't load. Not asked for again this round.</summary>
    private readonly HashSet<string> _missing = new();

    private readonly List<string> _due = new();

    private static readonly TextureLoadParameters Parameters =
        TextureLoadParameters.Default with { SampleParameters = TextureSampleParameters.Default };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PhotoImageEvent>(OnImage);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Reset();
    }

    /// <summary>The full image for a hash, asking the server for it the first time.</summary>
    public Texture? Get(string hash)
    {
        if (string.IsNullOrEmpty(hash))
            return null;

        if (_textures.TryGetValue(hash, out var texture))
            return texture;

        if (!_requested.ContainsKey(hash) && !_missing.Contains(hash))
            Request(hash);

        return null;
    }

    /// <summary>The thumbnail for a photo, built from its base64 RGB data the first time.</summary>
    public Texture? Thumbnail(PhotoComponent photo)
    {
        if (string.IsNullOrEmpty(photo.Hash) || string.IsNullOrEmpty(photo.Thumbnail))
            return null;

        if (_thumbnails.TryGetValue(photo.Hash, out var texture))
            return texture;

        var bytes = new byte[PhotoRules.ThumbnailBytes];
        if (!Convert.TryFromBase64String(photo.Thumbnail, bytes, out var written) || written != bytes.Length)
            return null;

        var size = PhotoRules.ThumbnailPixels;
        using var image = new Image<Rgba32>(size, size);
        for (var i = 0; i < size * size; i++)
        {
            image[i % size, i / size] = new Rgba32(bytes[i * 3], bytes[i * 3 + 1], bytes[i * 3 + 2]);
        }

        texture = Texture.LoadFromImage(image, "photo-thumbnail", Parameters);
        _thumbnails[photo.Hash] = texture;
        return texture;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.RealTime;
        foreach (var (hash, asked) in _requested)
        {
            if (now >= asked + RetryDelay)
                _due.Add(hash);
        }

        foreach (var hash in _due)
        {
            Request(hash);
        }

        _due.Clear();
    }

    private void Request(string hash)
    {
        _requested[hash] = _timing.RealTime;
        RaiseNetworkEvent(new PhotoImageRequestEvent(hash));
    }

    private void OnImage(PhotoImageEvent ev)
    {
        if (!_requested.Remove(ev.Hash))
            return;

        if (ev.Png == null || Load(ev.Png) is not { } texture)
        {
            _missing.Add(ev.Hash);
            return;
        }

        _textures[ev.Hash] = texture;
    }

    private Texture? Load(byte[] png)
    {
        try
        {
            using var image = Image.Load<Rgba32>(new MemoryStream(png));
            return Texture.LoadFromImage(image, "photo", Parameters);
        }
        catch (Exception e)
        {
            Log.Warning($"Unreadable photo image: {e.Message}");
            return null;
        }
    }

    private void Reset()
    {
        foreach (var texture in _textures.Values.Concat(_thumbnails.Values))
        {
            (texture as OwnedTexture)?.Dispose();
        }

        _textures.Clear();
        _thumbnails.Clear();
        _requested.Clear();
        _missing.Clear();
    }
}
