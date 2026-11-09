using System.IO;
using System.Numerics;
using Content.Client.Viewport;
using Content.Shared._WF.Photography;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.Utility;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.Photography;

/// <summary>Renders the photos the server asks for in an off-screen viewport and sends them back as PNG.</summary>
public sealed partial class PhotoCaptureSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private PhotoCaptureFilterSystem _filter = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    /// <summary>Frames waited before rendering, so the flash light's state has arrived.</summary>
    private const int WaitFrames = 2;

    private readonly CaptureControl _control = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PhotoCaptureRequestEvent>(OnRequest);
        _control.System = this;
        _ui.RootControl.AddChild(_control);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _ui.RootControl.RemoveChild(_control);
        _control.Queue.Clear();
    }

    private void OnRequest(PhotoCaptureRequestEvent ev)
    {
        if (ev.Tiles is < 1 or > 7 || ev.Visible.Length != ev.Tiles * ev.Tiles)
            return;

        _control.Queue.Add(new QueuedCapture(ev, WaitFrames));
    }

    /// <summary>Renders one request into a viewport and reads its pixels back.</summary>
    private void Render(PhotoCaptureRequestEvent ev)
    {
        if (!TryGetEntity(ev.Map, out var mapUid) || !TryComp<MapComponent>(mapUid, out var map))
            return;

        var pixels = ev.Tiles * PhotoRules.TilePixels;
        var viewport = _clyde.CreateViewport(new Vector2i(pixels, pixels),
            new TextureSampleParameters { Filter = false }, "wf-photo-capture");
        viewport.ClearColor = Robust.Shared.Maths.Color.Black;
        viewport.Eye = new ScalingViewport.ZEye(0f, 0f, 0f)
        {
            DrawLight = true,
            DrawFov = false,
            DrawParallax = false,
            Position = new MapCoordinates(ev.Centre, map.MapId),
            Rotation = -new Angle(ev.Rotation),
            Zoom = Vector2.One,
        };

        var square = new PhotoSquare(EntityUid.Invalid, ev.Centre, new Angle(ev.Rotation), ev.Tiles);
        var ghosts = ShowGhosts(ev.Ghosts);
        try
        {
            _filter.Hide(square, map.MapId, ev.Hidden);
            viewport.Render();
        }
        catch
        {
            viewport.Dispose();
            throw;
        }
        finally
        {
            _filter.Restore();
            HideGhosts(ghosts);
        }

        viewport.RenderTarget.CopyPixelsToMemory<Rgba32>(image =>
        {
            try
            {
                Finish(ev, image);
            }
            finally
            {
                viewport.Dispose();
            }
        });
    }

    private List<Entity<SpriteComponent>> ShowGhosts(NetEntity[] ghosts)
    {
        var shown = new List<Entity<SpriteComponent>>();
        foreach (var net in ghosts)
        {
            if (!TryGetEntity(net, out var ghost) || !TryComp<SpriteComponent>(ghost, out var sprite) || sprite.Visible)
                continue;

            _sprite.SetVisible((ghost.Value, sprite), true);
            shown.Add((ghost.Value, sprite));
        }

        return shown;
    }

    private void HideGhosts(List<Entity<SpriteComponent>> sprites)
    {
        foreach (var sprite in sprites)
        {
            _sprite.SetVisible((sprite.Owner, sprite.Comp), false);
        }
    }

    /// <summary>Blacks out unseen tiles, greys the photo when asked, and uploads it.</summary>
    private void Finish(PhotoCaptureRequestEvent ev, Image<Rgba32> image)
    {
        var span = image.GetPixelSpan();
        var width = image.Width;
        var black = new Rgba32(0, 0, 0, 255);
        for (var i = 0; i < span.Length; i++)
        {
            var x = i % width;
            var y = i / width;
            var tile = y / PhotoRules.TilePixels * ev.Tiles + x / PhotoRules.TilePixels;
            if (tile >= ev.Visible.Length || !ev.Visible[tile])
            {
                span[i] = black;
                continue;
            }

            var pixel = span[i];
            if (ev.Monochrome)
            {
                var grey = (byte) Math.Clamp(0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B, 0f, 255f);
                pixel = new Rgba32(grey, grey, grey, 255);
            }
            else
            {
                pixel.A = 255;
            }

            span[i] = pixel;
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        if (stream.Length > PhotoRules.MaxPngBytes)
            return;

        RaiseNetworkEvent(new PhotoCaptureResultEvent(ev.RequestId, stream.ToArray()));
    }

    /// <summary>A request waiting for its frames to pass.</summary>
    private sealed class QueuedCapture(PhotoCaptureRequestEvent request, int frames)
    {
        public readonly PhotoCaptureRequestEvent Request = request;
        public int Frames = frames;
    }

    /// <summary>An invisible control that renders queued photos during its draw, inside the frame.</summary>
    private sealed class CaptureControl : Control
    {
        public PhotoCaptureSystem System = default!;
        public readonly List<QueuedCapture> Queue = new();

        protected override void Draw(IRenderHandle handle)
        {
            base.Draw(handle);

            for (var i = Queue.Count - 1; i >= 0; i--)
            {
                var queued = Queue[i];
                if (--queued.Frames > 0)
                    continue;

                Queue.RemoveAt(i);
                System.Render(queued.Request);
            }
        }
    }
}
