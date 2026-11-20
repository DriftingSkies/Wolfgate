using System.Numerics;
using Content.Shared._WG.Paintings;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.GameTicking;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.Graphics;
using Robust.Shared.Timing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WG.Paintings;

/// <summary>
/// Draws finished paintings on canvas, frame and easel sprites, and downloads catalogue paintings for the art app.
/// </summary>
public sealed partial class WGPaintingArtSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>How long to wait before asking again, as the server skips requests over its rate limit.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>How long a painting the server had no pixels for is left alone, as a failed load may be temporary.</summary>
    private static readonly TimeSpan MissingDelay = TimeSpan.FromMinutes(2);

    /// <summary>Row of the easel's ledge, which a canvas on it rests on.</summary>
    private const int EaselLedge = 22;

    private static readonly TextureLoadParameters Parameters =
        TextureLoadParameters.Default with { SampleParameters = TextureSampleParameters.Default };

    /// <summary>Paintings laid on their sprite sheet, by hash.</summary>
    private readonly Dictionary<string, Texture> _sheets = new();

    /// <summary>Catalogue paintings at their own size, by hash.</summary>
    private readonly Dictionary<string, Texture> _art = new();

    private readonly Dictionary<string, TimeSpan> _requested = new();
    /// <summary>Paintings the server had no pixels for, with when they may be asked for again.</summary>
    private readonly Dictionary<string, TimeSpan> _missing = new();
    private readonly List<string> _due = new();

    /// <summary>Raised when a downloaded painting arrives, so open views can show it.</summary>
    public event Action? ArtArrived;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WGPaintingArtEvent>(OnArt);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => Reset());

        SubscribeLocalEvent<WGCanvasComponent, ComponentStartup>((uid, comp, _) => UpdateCanvas((uid, comp)));
        SubscribeLocalEvent<WGCanvasComponent, AfterAutoHandleStateEvent>((uid, comp, _) => UpdateCanvas((uid, comp)));
        SubscribeLocalEvent<WGPaintingFrameComponent, ComponentStartup>((uid, _, _) => UpdateHolder(uid));
        SubscribeLocalEvent<WGPaintingFrameComponent, EntInsertedIntoContainerMessage>((uid, _, _) => UpdateHolder(uid));
        SubscribeLocalEvent<WGPaintingFrameComponent, EntRemovedFromContainerMessage>((uid, _, _) => UpdateHolder(uid));
        SubscribeLocalEvent<WGEaselComponent, ComponentStartup>((uid, _, _) => UpdateHolder(uid));
        SubscribeLocalEvent<WGEaselComponent, EntInsertedIntoContainerMessage>((uid, _, _) => UpdateHolder(uid));
        SubscribeLocalEvent<WGEaselComponent, EntRemovedFromContainerMessage>((uid, _, _) => UpdateHolder(uid));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Reset();
    }

    private void Reset()
    {
        foreach (var texture in _sheets.Values)
            (texture as OwnedTexture)?.Dispose();
        foreach (var texture in _art.Values)
            (texture as OwnedTexture)?.Dispose();
        _sheets.Clear();
        _art.Clear();
        _requested.Clear();
        _missing.Clear();
    }

    /// <summary>A catalogue painting at its own size, or null while it downloads.</summary>
    public Texture? GetArt(string hash)
    {
        if (_art.TryGetValue(hash, out var texture))
            return texture;

        var now = _timing.RealTime;
        if (_missing.TryGetValue(hash, out var retry))
        {
            if (now < retry)
                return null;
            _missing.Remove(hash);
        }

        if (!_requested.ContainsKey(hash))
        {
            _requested[hash] = now;
            RaiseNetworkEvent(new WGPaintingArtRequestEvent(new List<string> { hash }));
        }

        return null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_requested.Count == 0)
            return;

        var now = _timing.RealTime;
        _due.Clear();
        foreach (var (hash, asked) in _requested)
        {
            if (now - asked > RetryDelay)
                _due.Add(hash);
        }

        if (_due.Count == 0)
            return;

        foreach (var hash in _due)
            _requested[hash] = now;
        for (var i = 0; i < _due.Count; i += WGPaintingRules.MaxRequestedArt)
        {
            RaiseNetworkEvent(new WGPaintingArtRequestEvent(
                _due.GetRange(i, Math.Min(WGPaintingRules.MaxRequestedArt, _due.Count - i))));
        }
    }

    private void OnArt(WGPaintingArtEvent ev)
    {
        _requested.Remove(ev.Hash);
        if (ev.Pixels == null || ev.Pixels.Length != ev.Width * ev.Height * 4 || ev.Width <= 0 || ev.Height <= 0)
        {
            _missing[ev.Hash] = _timing.RealTime + MissingDelay;
            return;
        }

        using var image = new Image<Rgba32>(ev.Width, ev.Height);
        Blit(image, ev.Pixels, ev.Width, ev.Height, 0, 0);
        _art[ev.Hash] = Texture.LoadFromImage(image, "painting", Parameters);
        ArtArrived?.Invoke();
    }

    private static void Blit(Image<Rgba32> image, byte[] pixels, int width, int height, int left, int top)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                image[left + x, top + y] = new Rgba32(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
            }
        }
    }

    /// <summary>A finished canvas's painting laid where its sprites show it, or null for an unfinished one.</summary>
    private Texture? Sheet(WGCanvasComponent canvas)
    {
        if (!canvas.Finished
            || canvas.Art is not { } pixels
            || pixels.Length != canvas.Width * canvas.Height * 4
            || WGPaintingRules.GetSize(canvas.Width, canvas.Height) is not { } size)
            return null;

        if (_sheets.TryGetValue(canvas.Hash, out var texture))
            return texture;

        using var image = new Image<Rgba32>(size.Sheet, size.Sheet);
        Blit(image, pixels, canvas.Width, canvas.Height, size.ArtX, size.ArtY);
        texture = Texture.LoadFromImage(image, "painting", Parameters);
        _sheets[canvas.Hash] = texture;
        return texture;
    }

    private void UpdateCanvas(Entity<WGCanvasComponent> ent)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
        {
            Entity<SpriteComponent?> spriteEnt = (ent.Owner, sprite);
            SetLayer(spriteEnt, WGPaintingVisualLayers.Wip, ent.Comp.Used && !ent.Comp.Finished);
            SetArt(spriteEnt, Sheet(ent.Comp));
        }

        if (_container.TryGetContainingContainer((ent.Owner, null, null), out var container))
            UpdateHolder(container.Owner);
    }

    /// <summary>Shows the canvas a frame or easel holds.</summary>
    private void UpdateHolder(EntityUid holder)
    {
        if (!TryComp<SpriteComponent>(holder, out var sprite))
            return;

        Entity<SpriteComponent?> spriteEnt = (holder, sprite);
        if (TryComp<WGPaintingFrameComponent>(holder, out _))
        {
            var canvas = Held(holder, WGPaintingFrameComponent.ContainerId);
            var size = canvas == null ? null : WGPaintingRules.GetSize(canvas.Width, canvas.Height);
            SetLayer(spriteEnt, WGPaintingVisualLayers.Canvas, size == null);
            SetLayer(spriteEnt, WGPaintingVisualLayers.Frame, size != null);
            if (size is { } frameSize)
                _sprite.LayerSetRsi(spriteEnt, WGPaintingVisualLayers.Frame, frameSize.Rsi, frameSize.State + "-frame");
            SetArt(spriteEnt, canvas == null ? null : Sheet(canvas));
            return;
        }

        if (!HasComp<WGEaselComponent>(holder))
            return;

        var held = _itemSlots.GetItemOrNull(holder, WGEaselComponent.SlotId) is { } uid
            ? CompOrNull<WGCanvasComponent>(uid)
            : null;
        var easelSize = held == null ? null : WGPaintingRules.GetSize(held.Width, held.Height);
        SetLayer(spriteEnt, WGPaintingVisualLayers.Canvas, easelSize != null);
        SetLayer(spriteEnt, WGPaintingVisualLayers.Wip, held is { Used: true, Finished: false });
        SetArt(spriteEnt, held == null ? null : Sheet(held));
        if (easelSize is not { } onEasel)
            return;

        // Rests the canvas's bottom edge on the easel's ledge.
        var bottom = onEasel.ArtY + onEasel.Height + 1 - (onEasel.Sheet - 32) / 2;
        var offset = new Vector2(0, (bottom - EaselLedge) / 32f);
        _sprite.LayerSetRsi(spriteEnt, WGPaintingVisualLayers.Canvas, onEasel.Rsi, onEasel.State);
        _sprite.LayerSetRsi(spriteEnt, WGPaintingVisualLayers.Wip, onEasel.Rsi, onEasel.State + "-wip");
        foreach (var layer in new[] { WGPaintingVisualLayers.Canvas, WGPaintingVisualLayers.Wip, WGPaintingVisualLayers.Art })
        {
            if (_sprite.LayerMapTryGet(spriteEnt, layer, out _, false))
                _sprite.LayerSetOffset(spriteEnt, layer, offset);
        }
    }

    private WGCanvasComponent? Held(EntityUid holder, string containerId)
    {
        return _container.TryGetContainer(holder, containerId, out var container)
            && container.ContainedEntities.Count > 0
                ? CompOrNull<WGCanvasComponent>(container.ContainedEntities[0])
                : null;
    }

    private void SetLayer(Entity<SpriteComponent?> sprite, WGPaintingVisualLayers layer, bool visible)
    {
        if (_sprite.LayerMapTryGet(sprite, layer, out _, false))
            _sprite.LayerSetVisible(sprite, layer, visible);
    }

    private void SetArt(Entity<SpriteComponent?> sprite, Texture? texture)
    {
        if (!_sprite.LayerMapTryGet(sprite, WGPaintingVisualLayers.Art, out _, false))
            return;

        _sprite.LayerSetVisible(sprite, WGPaintingVisualLayers.Art, texture != null);
        if (texture != null)
            _sprite.LayerSetTexture(sprite, WGPaintingVisualLayers.Art, texture);
    }
}
