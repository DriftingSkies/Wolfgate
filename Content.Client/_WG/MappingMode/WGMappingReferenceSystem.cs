using System.Numerics;
using Content.Client._WG.MappingTools;
using Content.Shared._WG.MappingMode;
using Content.Shared._WG.MappingTools;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Placement;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Client._WG.MappingMode;

/// <summary>
/// Placing a reference grid: after the server sends a picked file's footprint, its outline follows the cursor
/// snapped to tiles; R turns it, a click places it, Escape or right click drops it, and so does picking something in
/// the spawn menu.
/// </summary>
public sealed partial class WGMappingReferenceSystem : EntitySystem
{
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlacementManager _placement = default!;
    [Dependency] private MappingToolsSystem _tools = default!;
    [Dependency] private WGMappingClientSystem _mapping = default!;

    /// <summary>Keys the placement takes over while it runs; the MappingTools ones only exist while its window is open.</summary>
    private static readonly BoundKeyFunction[] Functions =
    {
        ContentKeyFunctions.WGMappingSelect,
        ContentKeyFunctions.WGMappingSelectCancel,
        ContentKeyFunctions.WGMappingRotate,
        ContentKeyFunctions.WGMappingReferenceCancel,
    };

    /// <summary>Floor plans kept before the cache starts over.</summary>
    private const int MaxCachedPreviews = 64;

    private readonly List<BoundKeyFunction> _added = new();
    private readonly Dictionary<string, WGMappingReferencePreview> _previews = new();

    /// <summary>Raised with the answer to <see cref="RequestPreview"/>.</summary>
    public event Action<WGMappingReferencePreviewEvent>? PreviewReceived;

    /// <summary>The footprint being placed, or null when nothing is.</summary>
    public WGMappingReferenceFootprintEvent? Footprint { get; private set; }

    /// <summary>Quarter turns clockwise.</summary>
    public int Turns { get; private set; }

    public bool Placing => Footprint != null;

    /// <summary>Raised when placement starts, so the Mapping window can step aside.</summary>
    public event Action? PlacementStarted;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<WGMappingReferenceFootprintEvent>(OnFootprint);
        SubscribeNetworkEvent<WGMappingReferencePreviewEvent>(OnPreview);
        _mapping.ResultReceived += OnResult;
        _mapping.SessionEnded += End;
        _mapping.SessionEnded += _previews.Clear;

        CommandBinds.Builder
            .BindBefore(ContentKeyFunctions.WGMappingSelect,
                new PointerInputCmdHandler((_, _, _) => Confirm(), outsidePrediction: true),
                typeof(MappingToolsSystem))
            .BindBefore(ContentKeyFunctions.WGMappingRotate,
                new PointerInputCmdHandler((_, _, _) => Rotate(), outsidePrediction: true),
                typeof(MappingToolsSystem))
            .BindBefore(ContentKeyFunctions.WGMappingSelectCancel,
                new PointerInputCmdHandler((_, _, _) => Cancel(), outsidePrediction: true),
                typeof(MappingToolsSystem))
            .Bind(ContentKeyFunctions.WGMappingReferenceCancel,
                new PointerInputCmdHandler((_, _, _) => Cancel(), outsidePrediction: true))
            .Register<WGMappingReferenceSystem>();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        End();
        _mapping.ResultReceived -= OnResult;
        _mapping.SessionEnded -= End;
        _mapping.SessionEnded -= _previews.Clear;
        CommandBinds.Unregister<WGMappingReferenceSystem>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (Placing && _placement.IsActive)
            Cancel();
    }

    /// <summary>Asks the server for the files that can be loaded as a reference.</summary>
    public void RequestList()
    {
        RaiseNetworkEvent(new WGMappingReferenceListRequest());
    }

    /// <summary>Gets a listed file's floor plan, from the server the first time.</summary>
    public void RequestPreview(string path)
    {
        if (_previews.TryGetValue(path, out var preview))
            PreviewReceived?.Invoke(new WGMappingReferencePreviewEvent(path, preview, null));
        else
            RaiseNetworkEvent(new WGMappingReferencePreviewRequest(path));
    }

    private void OnPreview(WGMappingReferencePreviewEvent ev)
    {
        if (ev.Preview != null)
        {
            if (_previews.Count >= MaxCachedPreviews)
                _previews.Clear();

            _previews[ev.Path] = ev.Preview;
        }

        PreviewReceived?.Invoke(ev);
    }

    /// <summary>Asks the server to load a listed file aside and send its footprint.</summary>
    public void Pick(string path)
    {
        RaiseNetworkEvent(new WGMappingReferencePickRequest(path));
    }

    private void OnFootprint(WGMappingReferenceFootprintEvent ev)
    {
        if (!_mapping.InSession)
            return;

        _placement.Clear();
        Footprint = ev;
        Turns = 0;
        SetFunctionsActive(true);
        if (!_overlays.HasOverlay<WGMappingReferenceOverlay>())
            _overlays.AddOverlay(new WGMappingReferenceOverlay(this));

        PlacementStarted?.Invoke();
    }

    private void OnResult(WGMappingResult result)
    {
        if (result.LocKey is "wg-mapping-mode-reference-placed" or "wg-mapping-mode-reference-expired")
            End();
    }

    /// <summary>Where the footprint's anchor sits: the mouse's map position, floored to a tile corner.</summary>
    public bool TryGetAnchor(out MapCoordinates anchor)
    {
        var mouse = _eye.PixelToMap(_input.MouseScreenPosition);
        anchor = new MapCoordinates(new Vector2(MathF.Floor(mouse.X), MathF.Floor(mouse.Y)), mouse.MapId);
        return mouse.MapId != MapId.Nullspace;
    }

    private bool Confirm()
    {
        if (Footprint is not { } footprint)
            return false;

        if (TryGetAnchor(out var anchor))
            RaiseNetworkEvent(new WGMappingReferenceConfirmRequest(footprint.Token, anchor.Position, Turns));

        return true;
    }

    private bool Rotate()
    {
        if (!Placing)
            return false;

        Turns = MappingToolsMath.Normalize(Turns + 1);
        return true;
    }

    /// <summary>Drops the reference being placed and tells the server to discard it.</summary>
    public bool Cancel()
    {
        if (Footprint is not { } footprint)
            return false;

        RaiseNetworkEvent(new WGMappingReferenceCancelRequest(footprint.Token));
        End();
        return true;
    }

    /// <summary>Stops placing without telling the server, which already dropped or placed it.</summary>
    private void End()
    {
        if (Footprint == null)
            return;

        Footprint = null;
        SetFunctionsActive(false);
        _overlays.RemoveOverlay<WGMappingReferenceOverlay>();
    }

    /// <summary>
    /// Makes the placement keys exist while it runs. Keys it added are taken away afterwards, except the MappingTools
    /// ones once its window has opened and owns them.
    /// </summary>
    private void SetFunctionsActive(bool active)
    {
        var context = _input.Contexts.GetContext("common");
        if (active)
        {
            foreach (var function in Functions)
            {
                if (context.FunctionExists(function))
                    continue;

                context.AddFunction(function);
                _added.Add(function);
            }

            return;
        }

        foreach (var function in _added)
        {
            var owned = _tools.Enabled && function != ContentKeyFunctions.WGMappingReferenceCancel;
            if (!owned && context.FunctionExists(function))
                context.RemoveFunction(function);
        }

        _added.Clear();
    }
}
