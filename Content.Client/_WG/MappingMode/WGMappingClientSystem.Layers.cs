using Content.Client._WG.MappingMode.Link;
using Content.Client.Decals;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.MappingMode;
using Robust.Shared.Network;
using Content.Client.Decals.Overlays;

namespace Content.Client._WG.MappingMode;

/// <summary>A view layer the mapping screen can show or hide on this client.</summary>
public enum WGMappingLayer : byte
{
    Walls,
    Subfloor,
    Decals,
    Markers,
    Lights,

    /// <summary>Signal links and device lists drawn as lines.</summary>
    Links,

    /// <summary>The world tile grid every level shares.</summary>
    WorldGrid,

    /// <summary>Levels below drawn smaller, as on a planet.</summary>
    Perspective,
}

public sealed partial class WGMappingClientSystem
{
    [Dependency] private DecalSystem _decalSystem = default!;
    [Dependency] private IClientNetManager _netManager = default!;
    [Dependency] private WGMappingLinkSystem _link = default!;

    /// <summary>The link system, for the Layers panel's count of what the Links layer draws.</summary>
    public WGMappingLinkSystem Links => _link;

    public static readonly WGMappingLayer[] Layers =
    {
        WGMappingLayer.Walls,
        WGMappingLayer.Subfloor,
        WGMappingLayer.Decals,
        WGMappingLayer.Markers,
        WGMappingLayer.Lights,
        WGMappingLayer.Links,
        WGMappingLayer.WorldGrid,
        WGMappingLayer.Perspective,
    };

    /// <summary>Whether the Depth perspective layer is on.</summary>
    private bool _perspective;

    /// <summary>Whether a layer is shown.</summary>
    public bool GetLayer(WGMappingLayer layer)
    {
        return layer switch
        {
            WGMappingLayer.Walls => !_tools.WallsHidden,
            WGMappingLayer.Subfloor => _subFloor.ShowAll,
            WGMappingLayer.Decals => _overlays.HasOverlay<DecalOverlay>(),
            WGMappingLayer.Markers => _markers.MarkersVisible,
            WGMappingLayer.Links => _link.ShowAll,
            WGMappingLayer.WorldGrid => _overlays.HasOverlay<WGMappingWorldGridOverlay>(),
            WGMappingLayer.Perspective => _perspective,
            _ => _light.Enabled,
        };
    }

    /// <summary>Shows or hides a layer.</summary>
    public void SetLayer(WGMappingLayer layer, bool shown)
    {
        switch (layer)
        {
            case WGMappingLayer.Walls:
                _tools.SetWallsHidden(!shown);
                break;
            case WGMappingLayer.Subfloor:
                // The setter asks the server too, which logs an error once the client has disconnected.
                if (_subFloor.ShowAll != shown && _netManager.IsConnected)
                    _subFloor.ShowAll = shown;
                break;
            case WGMappingLayer.Decals:
                if (GetLayer(WGMappingLayer.Decals) != shown)
                    _decalSystem.ToggleOverlay();
                break;
            case WGMappingLayer.Markers:
                _markers.MarkersVisible = shown;
                break;
            case WGMappingLayer.Lights:
                _light.Enabled = shown;
                break;
            case WGMappingLayer.Links:
                _link.SetShowAll(shown);
                break;
            case WGMappingLayer.WorldGrid:
                if (!shown)
                    _overlays.RemoveOverlay<WGMappingWorldGridOverlay>();
                else if (!_overlays.HasOverlay<WGMappingWorldGridOverlay>())
                    _overlays.AddOverlay(new WGMappingWorldGridOverlay(this));
                break;
            case WGMappingLayer.Perspective:
                _perspective = shown;
                UpdatePerspective();
                break;
        }
    }

    /// <summary>
    /// Puts every layer back: the mapping defaults (markers, subfloor, world grid, no lighting, no links) or the normal
    /// game's.
    /// </summary>
    private void ResetLayers(bool mapping)
    {
        SetLayer(WGMappingLayer.Walls, true);
        SetLayer(WGMappingLayer.Decals, true);
        SetLayer(WGMappingLayer.Subfloor, mapping);
        SetLayer(WGMappingLayer.Markers, mapping);
        SetLayer(WGMappingLayer.Lights, !mapping);
        SetLayer(WGMappingLayer.Links, false);
        SetLayer(WGMappingLayer.WorldGrid, mapping);
        SetLayer(WGMappingLayer.Perspective, false);
    }

    /// <summary>Puts the perspective marker on every level while the layer is on, and takes it off otherwise.</summary>
    private void UpdatePerspective()
    {
        var query = AllEntityQuery<CEZMapComponent>(); // All: session levels are paused
        while (query.MoveNext(out var uid, out _))
        {
            if (_perspective)
                EnsureComp<WGMappingPerspectiveComponent>(uid);
            else
                RemComp<WGMappingPerspectiveComponent>(uid);
        }
    }
}
